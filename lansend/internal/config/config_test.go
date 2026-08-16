package config

import (
	"io"
	"testing"
)

func TestParseSize(t *testing.T) {
	for input, want := range map[string]int64{"1K": 1024, "2M": 2 << 20, "3G": 3 << 30, "42": 42} {
		got, e := ParseSize(input)
		if e != nil || got != want {
			t.Fatalf("%s: %d %v", input, got, e)
		}
	}
}

func TestQRCodeIsOptIn(t *testing.T) {
	defaults, _, err := Parse(nil, io.Discard)
	if err != nil || defaults.QRCode {
		t.Fatalf("default qrcode: %v, %v", defaults.QRCode, err)
	}
	for _, flag := range []string{"--qrcode", "--qr"} {
		enabled, _, err := Parse([]string{flag}, io.Discard)
		if err != nil || !enabled.QRCode {
			t.Fatalf("%s qrcode: %v, %v", flag, enabled.QRCode, err)
		}
	}
}
