package server

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"io/fs"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"

	"github.com/lansend/lansend/internal/auth"
	"github.com/lansend/lansend/internal/config"
	"github.com/lansend/lansend/internal/files"
	"github.com/lansend/lansend/internal/texthub"
)

type Server struct {
	cfg   config.Config
	store *files.Store
	text  *texthub.Hub
	ui    fs.FS
}

const maxTextPreview = int64(1 << 20)

func New(c config.Config, s *files.Store, h *texthub.Hub, ui fs.FS) *Server {
	return &Server{c, s, h, ui}
}
func (s *Server) Handler() http.Handler {
	m := http.NewServeMux()
	m.HandleFunc("/enter", s.enter)
	m.HandleFunc("/api/v1/", s.api)
	m.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		http.FileServer(http.FS(s.ui)).ServeHTTP(w, r)
	})
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("X-Content-Type-Options", "nosniff")
		w.Header().Set("Content-Security-Policy", "default-src 'self'; style-src 'self'; script-src 'self'")
		m.ServeHTTP(w, r)
	})
}
func (s *Server) enter(w http.ResponseWriter, r *http.Request) {
	if s.cfg.Token == "" {
		http.Redirect(w, r, "/", 302)
		return
	}
	token := r.URL.Query().Get("token")
	next := "/"
	if r.Method == http.MethodPost {
		token = r.FormValue("token")
		next = safeNext(r.FormValue("next"))
	}
	if !auth.Equal(token, s.cfg.Token) {
		if r.Method == http.MethodPost {
			http.Redirect(w, r, "/login.html?error=1&next="+url.QueryEscape(next), http.StatusSeeOther)
			return
		}
		s.error(w, http.StatusUnauthorized, "unauthorized", "访问令牌无效")
		return
	}
	http.SetCookie(w, auth.Session(s.cfg.Token, r.TLS != nil))
	http.Redirect(w, r, next, http.StatusSeeOther)
}

func safeNext(next string) string {
	if strings.HasPrefix(next, "/") && !strings.HasPrefix(next, "//") {
		return next
	}
	return "/"
}
func (s *Server) api(w http.ResponseWriter, r *http.Request) {
	if !auth.Authorized(r, s.cfg.Token, s.cfg.Token == "") {
		s.error(w, 401, "unauthorized", "需要访问令牌")
		return
	}
	p := strings.TrimPrefix(r.URL.Path, "/api/v1/")
	switch {
	case p == "status" && r.Method == "GET":
		s.json(w, 200, map[string]any{"uploadEnabled": !s.cfg.NoUpload, "downloadEnabled": !s.cfg.NoDownload, "downloadDir": s.store.DownloadDir()})
	case p == "files":
		s.list(w, r)
	case strings.HasPrefix(p, "files/"):
		s.download(w, r, strings.TrimPrefix(p, "files/"))
	case p == "upload" && r.Method == "POST":
		s.upload(w, r)
	case p == "upload-text":
		s.textAPI(w, r)
	default:
		s.error(w, 404, "not_found", "资源不存在")
	}
}
func (s *Server) list(w http.ResponseWriter, r *http.Request) {
	if s.cfg.NoDownload {
		s.error(w, 403, "download_disabled", "下载已关闭")
		return
	}
	if r.Method != "GET" {
		w.WriteHeader(405)
		return
	}
	id := r.URL.Query().Get("path")
	xs, e := s.store.List(id)
	if e != nil {
		s.error(w, 404, "not_found", "目录不存在")
		return
	}
	pathName, _ := s.store.PathName(id)
	pathName = filepath.ToSlash(pathName)
	breadcrumbs := []map[string]string{{"name": "下载目录", "id": ""}}
	parts := strings.Split(pathName, "/")
	for i := range parts {
		if parts[i] == "" {
			continue
		}
		breadcrumbs = append(breadcrumbs, map[string]string{
			"name": parts[i],
			"id":   files.ID(strings.Join(parts[:i+1], "/")),
		})
	}
	s.json(w, 200, map[string]any{"files": xs, "path": id, "pathName": pathName, "breadcrumbs": breadcrumbs})
}
func (s *Server) download(w http.ResponseWriter, r *http.Request, id string) {
	if s.cfg.NoDownload {
		s.error(w, 403, "download_disabled", "下载已关闭")
		return
	}
	if r.Method != "GET" && r.Method != "HEAD" {
		w.WriteHeader(405)
		return
	}
	f, i, e := s.store.Open(id)
	if e != nil {
		s.error(w, 404, "not_found", "文件不存在")
		return
	}
	defer f.Close()
	preview := r.URL.Query().Get("preview")
	disposition := "attachment"
	if preview != "" {
		disposition = "inline"
	}
	w.Header().Set("Content-Disposition", disposition+"; filename*=UTF-8''"+url.PathEscape(i.Name()))
	if preview == "text" && i.Size() > maxTextPreview {
		w.Header().Set("X-LanSend-Preview-Truncated", "true")
		w.Header().Set("X-LanSend-Original-Size", fmt.Sprintf("%d", i.Size()))
		http.ServeContent(w, r, i.Name(), i.ModTime(), io.NewSectionReader(f, 0, maxTextPreview))
		return
	}
	http.ServeContent(w, r, i.Name(), i.ModTime(), f)
}
func (s *Server) upload(w http.ResponseWriter, r *http.Request) {
	if s.cfg.NoUpload {
		s.error(w, 403, "upload_disabled", "上传已关闭")
		return
	}
	r.Body = http.MaxBytesReader(w, r.Body, s.cfg.MaxFile+(1<<20))
	if e := r.ParseMultipartForm(1 << 20); e != nil {
		s.error(w, 413, "file_too_large", "文件超过允许的大小")
		return
	}
	f, h, e := r.FormFile("file")
	if e != nil {
		s.error(w, 400, "invalid_request", "需要 file 表单字段")
		return
	}
	defer f.Close()
	i, e := s.store.Upload(h.Filename, f, h.Size)
	if errors.Is(e, files.ErrTooLarge) {
		s.error(w, 413, "file_too_large", "文件超过允许的大小")
		return
	}
	if errors.Is(e, files.ErrFull) {
		s.error(w, 507, "storage_full", "存储空间不足")
		return
	}
	if e != nil {
		s.error(w, 400, "upload_failed", e.Error())
		return
	}
	s.json(w, 201, map[string]any{"file": i})
}
func (s *Server) textAPI(w http.ResponseWriter, r *http.Request) {
	if s.cfg.NoUpload {
		s.error(w, 403, "upload_disabled", "文本上传已关闭")
		return
	}
	if r.Method == "PUT" {
		b, e := io.ReadAll(http.MaxBytesReader(w, r.Body, s.cfg.ClipboardLimit+1))
		if e != nil {
			s.error(w, 413, "text_too_large", "文本超过允许的大小")
			return
		}
		st, ok := s.text.Put(string(b))
		if !ok {
			s.error(w, 413, "text_too_large", "文本超过允许的大小")
			return
		}
		if e := os.MkdirAll(s.store.UploadDir(), 0700); e == nil {
			e = os.WriteFile(filepath.Join(s.store.UploadDir(), "upload-text"), b, 0600)
			if e != nil {
				s.error(w, 500, "write_failed", "无法保存上传文本")
				return
			}
		} else {
			s.error(w, 500, "write_failed", "无法创建上传目录")
			return
		}
		s.json(w, 200, map[string]any{"characters": st.Characters, "updated": st.Updated})
		return
	}
	w.WriteHeader(405)
}
func (s *Server) json(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	json.NewEncoder(w).Encode(v)
}
func (s *Server) error(w http.ResponseWriter, status int, code, msg string) {
	s.json(w, status, map[string]any{"error": map[string]string{"code": code, "message": msg}})
}
