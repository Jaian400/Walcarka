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

	rout := chi.NewRouter()
	rout.Use(middleware.Logger)

	rout.Get("/machine-state", handler.GetMachineStateHandler)

	addr := fmt.Sprintf(":%s", cfg.Port)
	log.Printf("Listen on http://localhost%s", addr)

	err := http.ListenAndServe(addr, rout)
	if err != nil {
		log.Fatalf("Error: %v", err)
	}
}
