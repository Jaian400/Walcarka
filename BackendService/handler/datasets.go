package handler

import (
	"BackendService/config"
	"log"
	"os"
	"strings"
)

type Dataset struct {
	ID       int    `json:"id"`
	Datetime string `json:"datetime"`
}

func ListDatasets(cfg *config.Config) []Dataset {
	files, err := os.ReadDir(cfg.StaticPath)
	if err != nil {
		log.Printf("Error reading directory: %v", err)
		return nil
	}
	var datasets []Dataset
	for i, file := range files {
		if !file.IsDir() {
			datetime := strings.TrimSuffix(strings.TrimPrefix(file.Name(), "WalcarkaDuo_"), ".csv")
			datasets = append(datasets, Dataset{
				ID:       i,
				Datetime: datetime,
			})
		}
	}
	return datasets
}
