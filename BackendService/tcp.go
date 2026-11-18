package main

import (
	"BackendService/config"
	"BackendService/handler"
	"bufio"
	"crypto/rand"
	"encoding/binary"
	"fmt"
	"io"
	"log"
	"net"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

func startTCPServer(cfg *config.Config, datasets *handler.DatasetStore) {
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
		go handleTCPClient(cfg, datasets, conn)
	}
}

func handleTCPClient(cfg *config.Config, datasets *handler.DatasetStore, conn net.Conn) {
	defer conn.Close()
	log.Println("TCP Connection started")
	remoteAddr := conn.RemoteAddr().String()
	log.Printf("Client: %s\n", remoteAddr)

	updates, unwatch := datasets.Watch()
	defer unwatch()
	done := make(chan struct{})
	defer close(done)
	type incomingRequest struct {
		message ClientMessage
		err     error
	}
	requests := make(chan incomingRequest, 1)
	go func() {
		for {
			var request ClientMessage
			err := readMessage(conn, &request)
			select {
			case requests <- incomingRequest{message: request, err: err}:
			case <-done:
				return
			}
			if err != nil {
				return
			}
		}
	}()

	followLatest := false
	selectedID := -1
	for {
		var request ClientMessage
		automatic := false
		select {
		case incoming := <-requests:
			if incoming.err != nil {
				if incoming.err != io.EOF {
					log.Printf("Reading request from %s failed: %v", remoteAddr, incoming.err)
				}
				return
			}
			request = incoming.message
			if request.Type == messageSubscribe && request.SendData != nil && !*request.SendData {
				followLatest = request.FollowLatest
				continue
			}
		case <-updates:
			if !followLatest {
				continue
			}
			request = ClientMessage{Type: messageSubscribe, FollowLatest: true}
			automatic = true
		}
		dataset, err := datasetForRequest(datasets, request)
		if err != nil {
			if err := writeMessage(conn, ErrorMessage{Type: messageError, Message: err.Error()}); err != nil {
				return
			}
			continue
		}
		if automatic && dataset.ID == selectedID {
			continue
		}
		followLatest = request.FollowLatest
		if err := sendData(cfg, conn, dataset); err != nil {
			log.Printf("Dataset transfer to %s failed: %v", remoteAddr, err)
			return
		}
		selectedID = dataset.ID
	}
}

func datasetForRequest(store *handler.DatasetStore, request ClientMessage) (handler.Dataset, error) {
	if request.Type != messageSubscribe {
		return handler.Dataset{}, fmt.Errorf("unsupported message type: %q", request.Type)
	}
	datasets := store.List()
	if request.FollowLatest {
		if len(datasets) == 0 {
			return handler.Dataset{}, fmt.Errorf("no datasets available")
		}
		return datasets[len(datasets)-1], nil
	}
	if request.DatasetID == nil {
		return handler.Dataset{}, fmt.Errorf("datasetId is required")
	}
	for _, dataset := range datasets {
		if dataset.ID == *request.DatasetID {
			return dataset, nil
		}
	}
	return handler.Dataset{}, fmt.Errorf("dataset %d not found", *request.DatasetID)
}

func sendData(cfg *config.Config, w io.Writer, dataset handler.Dataset) error {
	file, err := os.Open(filepath.Join(cfg.StaticPath, dataset.Filename))
	if err != nil {
		return err
	}
	defer file.Close()

	scanner := bufio.NewScanner(file)
	if !scanner.Scan() {
		if err := scanner.Err(); err != nil {
			return err
		}
		return fmt.Errorf("missing CSV header")
	}
	if err := writeMessage(w, DatasetStartMessage{
		Type: messageDatasetStart, DatasetID: dataset.ID,
	}); err != nil {
		return err
	}

	count := 0
	for scanner.Scan() {
		data := parseCsvData(scanner.Text())
		if err := writeMessage(w, MeasurementMessage{
			Type: messageMeasurement, RollerData: data,
		}); err != nil {
			return err
		}
		count++
	}
	if err := scanner.Err(); err != nil {
		return err
	}
	return writeMessage(w, DatasetEndMessage{
		Type: messageDatasetEnd, DatasetID: dataset.ID, SampleCount: count,
	})
}

type RollerData struct {
	Time           string  `json:"time"`
	Velocity       float64 `json:"velocity"`
	Current        float64 `json:"current"`
	Torque         float64 `json:"torque"`
	Load           float64 `json:"load"`
	ForceRight     float64 `json:"forceRight"`
	ForceLeft      float64 `json:"forceLeft"`
	PositionRight  float64 `json:"positionRight"`
	PositionLeft   float64 `json:"positionLeft"`
	TemperatureIn  float64 `json:"temperatureIn"`
	TemperatureOut float64 `json:"temperatureOut"`
}

func parseCsvData(line string) *RollerData {
	line = strings.ReplaceAll(line, `"`, "")

	parts := strings.Split(line, ",")
	time := fmt.Sprintf("%s.%s", parts[1], parts[2])
	vel, _ := strconv.ParseFloat(parts[3], 64)
	cur, _ := strconv.ParseFloat(parts[4], 64)
	tor, _ := strconv.ParseFloat(parts[6], 64)
	load, _ := strconv.ParseFloat(parts[5], 64)
	forceRight, _ := strconv.ParseFloat(parts[7], 64)
	forceLeft, _ := strconv.ParseFloat(parts[8], 64)
	positionRight, _ := strconv.ParseFloat(parts[9], 64)
	positionLeft, _ := strconv.ParseFloat(parts[10], 64)
	temperatureIn, _ := strconv.ParseFloat(parts[11], 64)
	temperatureOut, _ := strconv.ParseFloat(parts[12], 64)

	data := RollerData{
		Time:           time,
		Velocity:       vel,
		Current:        cur,
		Torque:         tor,
		Load:           load,
		ForceRight:     forceRight,
		ForceLeft:      forceLeft,
		PositionRight:  positionRight,
		PositionLeft:   positionLeft,
		TemperatureIn:  temperatureIn,
		TemperatureOut: temperatureOut,
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
