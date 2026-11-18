package main

import (
	"encoding/binary"
	"encoding/json"
	"fmt"
	"io"
)

const (
	messageSubscribe    = "subscribe"
	messageDatasetStart = "dataset_start"
	messageMeasurement  = "measurement"
	messageDatasetEnd   = "dataset_end"
	messagePing         = "ping"
	messagePong         = "pong"
	messageError        = "error"
	maxMessageSize      = 64 * 1024
)

type ClientMessage struct {
	Type         string `json:"type"`
	FollowLatest bool   `json:"followLatest"`
	DatasetID    *int   `json:"datasetId,omitempty"`
	SendData     *bool  `json:"sendData,omitempty"`
}

type DatasetStartMessage struct {
	Type      string `json:"type"`
	DatasetID int    `json:"datasetId"`
}

type MeasurementMessage struct {
	Type string `json:"type"`
	*RollerData
}

type DatasetEndMessage struct {
	Type        string `json:"type"`
	DatasetID   int    `json:"datasetId"`
	SampleCount int    `json:"sampleCount"`
}

type HeartbeatMessage struct {
	Type string `json:"type"`
}

type ErrorMessage struct {
	Type    string `json:"type"`
	Message string `json:"message"`
}

func writeMessage(w io.Writer, message any) error {
	payload, err := json.Marshal(message)
	if err != nil {
		return err
	}
	if len(payload) > maxMessageSize {
		return fmt.Errorf("message exceeds %d bytes", maxMessageSize)
	}

	frame := make([]byte, 4+len(payload))
	binary.BigEndian.PutUint32(frame[:4], uint32(len(payload)))
	copy(frame[4:], payload)
	for len(frame) > 0 {
		n, err := w.Write(frame)
		if err != nil {
			return err
		}
		if n == 0 {
			return io.ErrShortWrite
		}
		frame = frame[n:]
	}
	return nil
}

func readMessage(r io.Reader, message any) error {
	var header [4]byte
	if _, err := io.ReadFull(r, header[:]); err != nil {
		return err
	}
	size := binary.BigEndian.Uint32(header[:])
	if size == 0 || size > maxMessageSize {
		return fmt.Errorf("invalid message size: %d", size)
	}
	payload := make([]byte, size)
	if _, err := io.ReadFull(r, payload); err != nil {
		return err
	}
	return json.Unmarshal(payload, message)
}
