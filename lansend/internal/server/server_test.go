package server

import (
	"io"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"

	"github.com/lansend/lansend/internal/config"
	"github.com/lansend/lansend/internal/files"
	"github.com/lansend/lansend/internal/texthub"
)

func TestLargeTextPreviewIsTruncated(t *testing.T) {
	root := t.TempDir()
	content := make([]byte, maxTextPreview+100)
	for i := range content {
		content[i] = 'a'
	}
	if err := os.WriteFile(filepath.Join(root, "large.log"), content, 0644); err != nil {
		t.Fatal(err)
	}
	cfg := config.Defaults()
	cfg.DownloadDir = root
	cfg.UploadDir = filepath.Join(root, "uploads")
	store, err := files.New(cfg.DownloadDir, cfg.UploadDir, cfg.MaxFile, cfg.MaxStorage)
	if err != nil {
		t.Fatal(err)
	}
	handler := New(cfg, store, texthub.New(cfg.ClipboardLimit), nil).Handler()
	recorder := httptest.NewRecorder()
	request := httptest.NewRequest("GET", "/api/v1/files/"+files.ID("large.log")+"?preview=text", nil)
	handler.ServeHTTP(recorder, request)
	response := recorder.Result()
	defer response.Body.Close()
	body, err := io.ReadAll(response.Body)
	if err != nil {
		t.Fatal(err)
	}
	if response.Header.Get("X-LanSend-Preview-Truncated") != "true" || int64(len(body)) != maxTextPreview {
		t.Fatalf("preview length=%d, truncated=%q", len(body), response.Header.Get("X-LanSend-Preview-Truncated"))
	}
}
