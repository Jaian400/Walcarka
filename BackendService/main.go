package main

import (
	"BackendService/config"
	"BackendService/handler"
	"fmt"
	"log"
	"net/http"

	"github.com/go-chi/chi"
	"github.com/go-chi/chi/middleware"
)

func main() {
	cfg := config.New()
	datasets, err := handler.NewDatasetStore(cfg)
	if err != nil {
		log.Fatalf("Loading datasets: %v", err)
	}

	go startTCPServer(cfg, datasets)
	go startUDPDiscovery(cfg)

	rout := chi.NewRouter()
	rout.Use(middleware.Logger)

	rout.Get("/machine-state", handler.GetMachineStateHandler)
	rout.Get("/random-data", handler.RandomData)

	rout.Get("/datasets", func(w http.ResponseWriter, r *http.Request) {
		handler.Datasets(w, r, datasets.List())
	})

	addr := fmt.Sprintf(":%s", cfg.Port)
	log.Printf("Listen on http://localhost%s", addr)

	err = http.ListenAndServe(addr, rout)
	if err != nil {
		log.Fatalf("Error: %v", err)
	}
}
