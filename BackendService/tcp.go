package main

import (
	"BackendService/config"
	"bufio"
	"crypto/rand"
	"encoding/binary"
	"fmt"
	"io"
	"log"
	"net"
	"os"
	"regexp"
	"time"
)

func startTCPServer(cfg *config.Config) {
	addr := fmt.Sprintf(":%s", cfg.PortTCP)
	ln, err := net.Listen("tcp", addr)
	if err != nil {
		log.Println(err)
		return
	}
	log.Printf("TCP Server listen on http://localhost%s", addr)

	for {
		conn, err := ln.Accept()
		if err != nil {
			continue
		}
		go handleTCPClient(cfg, conn)
	}
}

func handleTCPClient(cfg *config.Config, conn net.Conn) {
	defer conn.Close()
	log.Println("TCP Connection started")
	remoteAddr := conn.RemoteAddr().String()
	log.Printf("Client: %s\n", remoteAddr)

	sendData(cfg, conn)
}

func sendData(cfg *config.Config, conn net.Conn) {
	file, err := os.Open(cfg.StaticPath + "WalcarkaDuo_2024_12_05_13_56.csv")
	if err != nil {
		log.Println("Error while opening a file:", err)
		return
	}
	defer file.Close()

	scanner := bufio.NewScanner(file)
	for scanner.Scan() {
		line := scanner.Text()

		re := regexp.MustCompile(`"`)
		line = re.ReplaceAllString(line, " ")

		log.Println(line)

		data := []byte(line)
		size := uint32(len(data))

		sizeBuf := make([]byte, 4)
		binary.BigEndian.PutUint32(sizeBuf, size)

		conn.Write(sizeBuf)
		conn.Write(data)

		// log.Println(string(data))

		time.Sleep(50 * time.Millisecond)
	}
}

func connectionTest(conn net.Conn) {
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
