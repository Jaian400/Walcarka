package main

import (
	"BackendService/config"
	"fmt"
	"log"
	"net"
	"strconv"
	"strings"
)

const DiscoverMsg = "DISCOVER_BACKEND_SERVICE"

func startUDPDiscovery(cfg *config.Config) {
	portInt, err := strconv.Atoi(cfg.PortUDP)
	if err != nil {
		log.Fatalf("Invalid UDP port in config: %v", err)
	}

	udpAddr := &net.UDPAddr{
		Port: portInt,
		IP:   net.ParseIP("0.0.0.0"),
	}

	conn, err := net.ListenUDP("udp", udpAddr)
	if err != nil {
		log.Printf("Error starting UDP Discovery: %v", err)
		return
	}
	defer conn.Close()

	log.Printf("UDP Discovery active on port %s", cfg.PortUDP)

	buffer := make([]byte, 1024)
	for {
		n, remoteAddr, err := conn.ReadFromUDP(buffer)
		if err != nil {
			continue
		}

		message := strings.TrimSpace(string(buffer[:n]))

		if message == DiscoverMsg {
			log.Printf("Discovery request from: %s", remoteAddr.String())

			// Tworzymy odpowiedź: VR_SERVER_ACK : Nazwa : Port
			response := fmt.Sprintf("VR_SERVER_ACK:%s:%s", cfg.ServerName, cfg.PortTCP)

			conn.WriteToUDP([]byte(response), remoteAddr)
		}
	}
}
