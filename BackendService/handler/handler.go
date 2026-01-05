package handler

import (
	"encoding/json"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

func getStaticPath(filename string) (string, error) {
	ex, err := os.Executable()
	if err != nil {
		return "", err
	}

	exPath := filepath.Dir(ex)

	fullPath := filepath.Join(exPath, "static", filename)

	return fullPath, nil
}

type MachineData struct {
	RollerSpeed float64 `json:"omega"`
	RollerGap   float64 `json:"gap"`
}

func GetMachineStateHandler(w http.ResponseWriter, r *http.Request) {
	path, err := getStaticPath("data.txt")
	if err != nil {
		http.Error(w, "Reading file error", http.StatusInternalServerError)
		return
	}

	content, err := os.ReadFile(path)

	if err != nil {
		http.Error(w, "Reading file error", http.StatusInternalServerError)
		return
	}

	lines := strings.Split(string(content), "\n")

	if len(lines) != 2 {
		http.Error(w, "Reading file error", http.StatusInternalServerError)
		return
	}

	omega, _ := strconv.ParseFloat(strings.TrimSpace(lines[0]), 64)
	gap, _ := strconv.ParseFloat(strings.TrimSpace(lines[1]), 64)

	data := MachineData{
		RollerSpeed: omega,
		RollerGap:   gap,
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(data)
}
