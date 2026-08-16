package files

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestValidName(t *testing.T) {
	for _, name := range []string{"report.pdf", "中文 文件.txt"} {
		if !ValidName(name) {
			t.Fatalf("valid name rejected: %q", name)
		}
	}
	for _, name := range []string{"", ".", "..", "../x", "a/b", "a\\b", "bad\nname"} {
		if ValidName(name) {
			t.Fatalf("invalid name accepted: %q", name)
		}
	}
}

func TestListAndOpenNestedDownloadFile(t *testing.T) {
	root := t.TempDir()
	if err := os.Mkdir(filepath.Join(root, "nested"), 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(root, "nested", "note.txt"), []byte("hello"), 0644); err != nil {
		t.Fatal(err)
	}
	s, err := New(root, filepath.Join(root, "uploads"), 100, 1000)
	if err != nil {
		t.Fatal(err)
	}
	items, err := s.List("")
	if err != nil || len(items) != 1 || !items[0].Directory {
		t.Fatalf("root list: %#v, %v", items, err)
	}
	items, err = s.List(items[0].ID)
	if err != nil || len(items) != 1 || items[0].Name != "note.txt" {
		t.Fatalf("nested list: %#v, %v", items, err)
	}
	f, _, err := s.Open(items[0].ID)
	if err != nil {
		t.Fatal(err)
	}
	defer f.Close()
}
func TestUploadKeepsBothNames(t *testing.T) {
	root := t.TempDir()
	s, err := New(root, root+"/uploads", 100, 1000)
	if err != nil {
		t.Fatal(err)
	}
	a, err := s.Upload("a.txt", strings.NewReader("one"), 3)
	if err != nil {
		t.Fatal(err)
	}
	b, err := s.Upload("a.txt", strings.NewReader("two"), 3)
	if err != nil {
		t.Fatal(err)
	}
	if a.Name != "a.txt" || b.Name != "a (1).txt" {
		t.Fatalf("unexpected names: %#v %#v", a, b)
	}
}

func TestListDetectsPreviewFromContent(t *testing.T) {
	root := t.TempDir()
	if err := os.WriteFile(filepath.Join(root, "LICENSE"), []byte("Permission is hereby granted\n"), 0644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(root, "archive.data"), []byte{0, 1, 2, 3, 0xff}, 0644); err != nil {
		t.Fatal(err)
	}
	s, err := New(root, filepath.Join(root, "uploads"), 100, 1000)
	if err != nil {
		t.Fatal(err)
	}
	items, err := s.List("")
	if err != nil {
		t.Fatal(err)
	}
	kinds := map[string]string{}
	for _, item := range items {
		kinds[item.Name] = item.Preview
	}
	if kinds["LICENSE"] != "text" || kinds["archive.data"] != "" {
		t.Fatalf("unexpected preview kinds: %#v", kinds)
	}
}
