package config

type Config struct {
	Port       string
	PortTCP    string
	PortUDP    string
	ServerName string
	StaticPath string
}

func New() *Config {
	return &Config{
		Port:       "8080",
		PortTCP:    "8081",
		PortUDP:    "9999",
		ServerName: "BackendService",
		StaticPath: "./static/",
	}
}
