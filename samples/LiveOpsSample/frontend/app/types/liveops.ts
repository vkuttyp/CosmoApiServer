export interface MetricSnapshot {
  cpu: number
  memory: number
  requests: number
  latencyMs: number
  timestamp: string
}

export interface LogEntry {
  level: 'info' | 'warn' | 'error'
  service: string
  message: string
  timestamp: string
}

export interface ServiceStatus {
  name: string
  status: 'healthy' | 'degraded' | 'down'
  uptime: string
  region: string
  replicas: number
}

export interface StatusResponse {
  environment: string
  version: string
  region: string
  services: ServiceStatus[]
}
