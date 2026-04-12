export default defineEventHandler((event) => {
  setHeader(event, 'Content-Type', 'application/javascript')
  return ''
})
