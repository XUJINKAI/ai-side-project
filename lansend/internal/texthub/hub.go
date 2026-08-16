package texthub

import (
	"sync"
	"time"
)

type Hub struct {
	mu      sync.RWMutex
	text    string
	version uint64
	updated time.Time
	limit   int64
}
type State struct {
	Text       string    `json:"text"`
	Version    uint64    `json:"version"`
	Updated    time.Time `json:"updated"`
	Characters int       `json:"characters"`
}

func New(limit int64) *Hub { return &Hub{limit: limit} }
func (h *Hub) Get() State {
	h.mu.RLock()
	defer h.mu.RUnlock()
	return State{h.text, h.version, h.updated, len([]rune(h.text))}
}
func (h *Hub) Put(s string) (State, bool) {
	if int64(len([]byte(s))) > h.limit {
		return State{}, false
	}
	h.mu.Lock()
	defer h.mu.Unlock()
	h.text = s
	h.version++
	h.updated = time.Now().UTC()
	return State{h.text, h.version, h.updated, len([]rune(h.text))}, true
}
