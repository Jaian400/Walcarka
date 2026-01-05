package config

type Config struct {
	Port       string
	StaticPath string
}

func New() *Config {
	return &Config{
		Port:       "8080",
		StaticPath: "./static",
	}
}
