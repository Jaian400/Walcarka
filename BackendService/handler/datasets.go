package handler

import (
	"BackendService/config"
	"os"
	"strings"
	"sync"
)

type Dataset struct {
	ID       int    `json:"id"`
	Datetime string `json:"datetime"`
	Filename string `json:"filename"`
}

type DatasetStore struct {
	mu       sync.RWMutex
	cfg      *config.Config
	datasets []Dataset
	ids      map[string]int
	nextID   int
	watchers map[chan struct{}]struct{}
}

func NewDatasetStore(cfg *config.Config) (*DatasetStore, error) {
	store := &DatasetStore{
		cfg: cfg, ids: make(map[string]int), watchers: make(map[chan struct{}]struct{}),
	}
	if err := store.Refresh(); err != nil {
		return nil, err
	}
	return store, nil
}

func (s *DatasetStore) Refresh() error {
	s.mu.Lock()
	defer s.mu.Unlock()

	files, err := os.ReadDir(s.cfg.StaticPath)
	if err != nil {
		return err
	}
	datasets := make([]Dataset, 0, len(files))
	for _, file := range files {
		name := file.Name()
		if file.IsDir() || !strings.HasPrefix(name, "WalcarkaDuo_") || !strings.HasSuffix(name, ".csv") {
			continue
		}
		id, exists := s.ids[name]
		if !exists {
			id = s.nextID
			s.nextID++
			s.ids[name] = id
		}
		datasets = append(datasets, Dataset{
			ID: id, Filename: name,
			Datetime: strings.TrimSuffix(strings.TrimPrefix(name, "WalcarkaDuo_"), ".csv"),
		})
	}
	s.datasets = datasets
	for watcher := range s.watchers {
		select {
		case watcher <- struct{}{}:
		default:
		}
	}
	return nil
}

func (s *DatasetStore) List() []Dataset {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return append([]Dataset{}, s.datasets...)
}

func (s *DatasetStore) Watch() (<-chan struct{}, func()) {
	updates := make(chan struct{}, 1)
	s.mu.Lock()
	s.watchers[updates] = struct{}{}
	s.mu.Unlock()
	return updates, func() {
		s.mu.Lock()
		delete(s.watchers, updates)
		s.mu.Unlock()
	}
}
