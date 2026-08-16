package auth

import (
	"crypto/rand"
	"crypto/subtle"
	"encoding/base64"
	"net/http"
	"strings"
)

const CookieName = "lansend_session"

func Generate() (string, error) {
	b := make([]byte, 32)
	if _, e := rand.Read(b); e != nil {
		return "", e
	}
	return base64.RawURLEncoding.EncodeToString(b), nil
}
func Equal(a, b string) bool {
	return len(a) == len(b) && subtle.ConstantTimeCompare([]byte(a), []byte(b)) == 1
}
func Authorized(r *http.Request, token string, noAuth bool) bool {
	if noAuth {
		return true
	}
	if c, e := r.Cookie(CookieName); e == nil && Equal(c.Value, token) {
		return true
	}
	return Equal(strings.TrimPrefix(r.Header.Get("Authorization"), "Bearer "), token)
}
func Session(token string, secure bool) *http.Cookie {
	return &http.Cookie{Name: CookieName, Value: token, Path: "/", HttpOnly: true, SameSite: http.SameSiteStrictMode, Secure: secure, MaxAge: 86400}
}
