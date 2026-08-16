package files

import (
	"crypto/rand"
	"encoding/base64"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"
	"unicode"
	"unicode/utf8"
)

type Item struct {
	ID        string    `json:"id"`
	Name      string    `json:"name"`
	Directory bool      `json:"directory"`
	Size      int64     `json:"size"`
	Modified  time.Time `json:"modified"`
	Preview   string    `json:"preview,omitempty"`
}
type Store struct {
	download, upload    string
	maxFile, maxStorage int64
	mu                  sync.Mutex
}

func New(download, upload string, maxFile, maxStorage int64) (*Store, error) {
	d, e := filepath.Abs(download)
	if e != nil {
		return nil, e
	}
	i, e := os.Stat(d)
	if e != nil || !i.IsDir() {
		return nil, fmt.Errorf("下载目录不可用: %s", d)
	}
	u, e := filepath.Abs(upload)
	if e != nil {
		return nil, e
	}
	return &Store{d, u, maxFile, maxStorage, sync.Mutex{}}, nil
}
func ValidName(n string) bool {
	if n == "" || n == "." || n == ".." || filepath.Base(n) != n || strings.ContainsAny(n, "\\/") || len(n) > 240 {
		return false
	}
	for _, r := range n {
		if unicode.IsControl(r) {
			return false
		}
	}
	return true
}
func ID(name string) string { return base64.RawURLEncoding.EncodeToString([]byte(name)) }
func Name(id string) (string, error) {
	b, e := base64.RawURLEncoding.DecodeString(id)
	p := filepath.Clean(string(b))
	if e != nil || p == "." || strings.HasPrefix(p, "..") || filepath.IsAbs(p) {
		return "", errors.New("invalid path")
	}
	return p, nil
}
func (s *Store) List(id string) ([]Item, error) {
	rel := ""
	if id != "" {
		var e error
		rel, e = Name(id)
		if e != nil {
			return nil, e
		}
	}
	root := filepath.Join(s.download, rel)
	ents, e := os.ReadDir(root)
	if e != nil {
		return nil, e
	}
	out := []Item{}
	for _, x := range ents {
		if x.Type()&os.ModeSymlink != 0 {
			continue
		}
		i, e := x.Info()
		if e != nil || (!i.IsDir() && !i.Mode().IsRegular()) {
			continue
		}
		p := filepath.Join(rel, x.Name())
		preview := ""
		if i.Mode().IsRegular() {
			preview = previewKind(filepath.Join(root, x.Name()))
		}
		out = append(out, Item{ID: ID(filepath.ToSlash(p)), Name: x.Name(), Directory: i.IsDir(), Size: i.Size(), Modified: i.ModTime(), Preview: preview})
	}
	sort.Slice(out, func(i, j int) bool {
		if out[i].Directory != out[j].Directory {
			return out[i].Directory
		}
		return strings.ToLower(out[i].Name) < strings.ToLower(out[j].Name)
	})
	return out, nil
}

func previewKind(path string) string {
	f, err := os.Open(path)
	if err != nil {
		return ""
	}
	defer f.Close()
	sample := make([]byte, 8192)
	n, _ := f.Read(sample)
	sample = sample[:n]
	contentType := http.DetectContentType(sample)
	if strings.HasPrefix(contentType, "image/") {
		return "image"
	}
	if strings.HasPrefix(contentType, "text/") || probablyText(sample) {
		return "text"
	}
	return ""
}

func probablyText(sample []byte) bool {
	if len(sample) == 0 || utf8.Valid(sample) {
		return true
	}
	if len(sample) >= 2 && ((sample[0] == 0xff && sample[1] == 0xfe) || (sample[0] == 0xfe && sample[1] == 0xff)) {
		return true
	}
	evenNUL, oddNUL := 0, 0
	for i, b := range sample {
		if b == 0 && i%2 == 0 {
			evenNUL++
		} else if b == 0 {
			oddNUL++
		}
	}
	if evenNUL*4 > len(sample) || oddNUL*4 > len(sample) {
		return true
	}
	controls := 0
	for _, b := range sample {
		if b == 0 {
			return false
		}
		if b < 0x20 && b != '\n' && b != '\r' && b != '\t' && b != '\f' {
			controls++
		}
	}
	return controls*50 <= len(sample)
}
func (s *Store) Open(id string) (*os.File, os.FileInfo, error) {
	rel, e := Name(id)
	if e != nil {
		return nil, nil, e
	}
	p := filepath.Join(s.download, rel)
	i, e := os.Lstat(p)
	if e != nil || i.Mode()&os.ModeSymlink != 0 || !i.Mode().IsRegular() {
		return nil, nil, os.ErrNotExist
	}
	f, e := os.Open(p)
	return f, i, e
}
func (s *Store) Upload(name string, r io.Reader, length int64) (Item, error) {
	if !ValidName(name) {
		return Item{}, errors.New("invalid filename")
	}
	if length > s.maxFile {
		return Item{}, ErrTooLarge
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if e := os.MkdirAll(s.upload, 0700); e != nil {
		return Item{}, e
	}
	usage, _ := dirSize(s.upload)
	if usage >= s.maxStorage {
		return Item{}, ErrFull
	}
	b := make([]byte, 12)
	rand.Read(b)
	tmp := filepath.Join(s.upload, ".upload-"+base64.RawURLEncoding.EncodeToString(b))
	f, e := os.OpenFile(tmp, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if e != nil {
		return Item{}, e
	}
	defer func() { f.Close(); os.Remove(tmp) }()
	limit := s.maxFile
	if s.maxStorage-usage < limit {
		limit = s.maxStorage - usage
	}
	n, e := io.Copy(f, io.LimitReader(r, limit+1))
	if e != nil {
		return Item{}, e
	}
	if n > limit {
		return Item{}, ErrTooLarge
	}
	if e = f.Close(); e != nil {
		return Item{}, e
	}
	dest := filepath.Join(s.upload, name)
	for x := 1; ; x++ {
		if _, e = os.Lstat(dest); os.IsNotExist(e) {
			break
		}
		ext := filepath.Ext(name)
		dest = filepath.Join(s.upload, fmt.Sprintf("%s (%d)%s", strings.TrimSuffix(name, ext), x, ext))
	}
	if e = os.Rename(tmp, dest); e != nil {
		return Item{}, e
	}
	i, _ := os.Stat(dest)
	return Item{ID: ID(filepath.Base(dest)), Name: filepath.Base(dest), Size: i.Size(), Modified: i.ModTime()}, nil
}
func (s *Store) UploadDir() string   { return s.upload }
func (s *Store) DownloadDir() string { return s.download }
func (s *Store) PathName(id string) (string, error) {
	if id == "" {
		return "", nil
	}
	return Name(id)
}
func dirSize(p string) (int64, error) {
	var n int64
	e := filepath.WalkDir(p, func(_ string, d os.DirEntry, e error) error {
		if e == nil && !d.IsDir() {
			i, _ := d.Info()
			if i != nil {
				n += i.Size()
			}
		}
		return e
	})
	return n, e
}

var ErrTooLarge = errors.New("file too large")
var ErrFull = errors.New("storage full")
