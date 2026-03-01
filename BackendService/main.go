package main

import (
	"BackendService/config"
	"BackendService/handler"
	"crypto/rand"
	"encoding/binary"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"

	"github.com/go-chi/chi"
	"github.com/go-chi/chi/middleware"
)

func main() {
	cfg := config.New()

	go startTCPServer()

	rout := chi.NewRouter()
	rout.Use(middleware.Logger)

	rout.Get("/machine-state", handler.GetMachineStateHandler)
	rout.Get("/random-data", handler.RandomData)

	addr := fmt.Sprintf(":%s", cfg.Port)
	log.Printf("Listen on http://localhost%s", addr)

	err := http.ListenAndServe(addr, rout)
	if err != nil {
		log.Fatalf("Error: %v", err)
	}
}

func startTCPServer() {
	ln, err := net.Listen("tcp", ":8081")
	if err != nil {
		log.Println(err)
		return
	}
	log.Println("Server TCP listen on http://localhost:8081...")

	for {
		conn, err := ln.Accept()
		if err != nil {
			continue
		}
		go handleTCPClient(conn)
	}
}

func handleTCPClient(conn net.Conn) {
	defer conn.Close()
	log.Println("TCP Connection started")
	remoteAddr := conn.RemoteAddr().String()
	log.Printf("Client: %s\n", remoteAddr)

	for {
		sizeBuf := make([]byte, 4)
		_, err := io.ReadFull(conn, sizeBuf)
		if err != nil {
			return
		}
		requestedSize := binary.BigEndian.Uint32(sizeBuf)
		log.Printf("Sending batch: %d bytes ...\n", requestedSize)

		payload := make([]byte, requestedSize)
		rand.Read(payload)

		_, err = conn.Write(payload)
		if err != nil {
			return
		}
	}
}
