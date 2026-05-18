package main

import (
	"BackendService/config"
	"bufio"
	"crypto/rand"
	"encoding/binary"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net"
	"os"
	"strconv"
	"strings"
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
	file, err := os.Open(cfg.StaticPath + "WalcarkaDuo_2025_05_26_13_57.csv")
	if err != nil {
		log.Println("Error while opening a file:", err)
		return
	}
	defer file.Close()

	scanner := bufio.NewScanner(file)
	for scanner.Scan() {
		line := scanner.Text()

		data := parseCsvData(line)

		jsonBytes, err := json.Marshal(data)
		if err != nil {
			continue
		}

		size := uint32(len(jsonBytes))
		sizeBuf := make([]byte, 4)
		binary.BigEndian.PutUint32(sizeBuf, size)

		conn.Write(sizeBuf)
		conn.Write(jsonBytes)

		// time.Sleep(50 * time.Millisecond)
	}
}

type RollerData struct {
	Time     string  `json:"time"`
	Velocity float64 `json:"velocity"`
	Current  float64 `json:"current"`
	Torque   float64 `json:"torque"`
}

func parseCsvData(line string) *RollerData {
	line = strings.ReplaceAll(line, `"`, "")

	parts := strings.Split(line, ",")
	time := fmt.Sprintf("%s.%s", parts[1], parts[2])
	vel, _ := strconv.ParseFloat(parts[3], 64)
	cur, _ := strconv.ParseFloat(parts[4], 64)
	tor, _ := strconv.ParseFloat(parts[6], 64)

	data := RollerData{
		Time:     time,
		Velocity: vel,
		Current:  cur,
		Torque:   tor,
	}

	return &data
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
