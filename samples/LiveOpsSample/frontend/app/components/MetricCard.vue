<template>
  <div class="rounded-xl border border-gray-800 bg-gray-900 p-4 flex flex-col gap-2">
    <span class="text-xs font-medium text-gray-400 uppercase tracking-widest">{{ label }}</span>
    <div class="flex items-end gap-1">
      <span class="text-3xl font-bold tabular-nums" :class="valueClass">{{ display }}</span>
      <span class="text-sm text-gray-500 mb-1">{{ unit }}</span>
    </div>
    <div v-if="unit === '%'" class="h-1.5 rounded-full bg-gray-800 overflow-hidden">
      <div
        class="h-full rounded-full transition-all duration-700"
        :class="barClass"
        :style="{ width: `${Math.min(value, 100)}%` }"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
const props = defineProps<{
  label: string
  value: number
  unit: string
  warn?: number
  danger?: number
}>()

const display = computed(() => Number.isFinite(props.value) ? props.value.toLocaleString() : '—')

const level = computed(() => {
  if (props.danger && props.value >= props.danger) return 'danger'
  if (props.warn   && props.value >= props.warn)   return 'warn'
  return 'ok'
})

const valueClass = computed(() => ({
  'text-red-400':     level.value === 'danger',
  'text-amber-400':   level.value === 'warn',
  'text-emerald-400': level.value === 'ok',
}))

const barClass = computed(() => ({
  'bg-red-500':     level.value === 'danger',
  'bg-amber-400':   level.value === 'warn',
  'bg-emerald-400': level.value === 'ok',
}))
</script>
