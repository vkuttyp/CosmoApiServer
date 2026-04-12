import type { MetricSnapshot, LogEntry } from '~/types/liveops'

const MAX_LOG_ENTRIES = 100

/**
 * Opens an EventSource to the given SSE endpoint and calls the handler for
 * every event with the specified name. Closes the connection when the
 * component is unmounted.
 */
function useSseEvent<T>(url: string, eventName: string, onEvent: (data: T) => void) {
  let source: EventSource | null = null

  onMounted(() => {
    source = new EventSource(url)
    source.addEventListener(eventName, (e: MessageEvent) => {
      try {
        onEvent(JSON.parse(e.data) as T)
      } catch {
        // ignore malformed events
      }
    })
  })

  onUnmounted(() => {
    source?.close()
  })
}

export function useMetricStream() {
  const config = useRuntimeConfig()
  const latest = ref<MetricSnapshot | null>(null)
  const history = ref<MetricSnapshot[]>([])

  useSseEvent<MetricSnapshot>(
    `${config.public.apiBase}/api/live/metrics`,
    'metric',
    (snap) => {
      latest.value = snap
      history.value = [...history.value.slice(-59), snap]
    }
  )

  return { latest, history }
}

export function useLogStream() {
  const config = useRuntimeConfig()
  const entries = ref<LogEntry[]>([])

  useSseEvent<LogEntry>(
    `${config.public.apiBase}/api/live/logs`,
    'log',
    (entry) => {
      entries.value = [entry, ...entries.value.slice(0, MAX_LOG_ENTRIES - 1)]
    }
  )

  return { entries }
}
