export type Metric = {
  title: string
  value: string
  caption: string
  tone: 'primary' | 'secondary' | 'warning' | 'success' | 'error' | 'neutral'
}

export type Workspace = {
  name: string
  region: string
  members: number
  status: string
}

export type TimelineItem = {
  title: string
  description: string
  at: string
}

export type DashboardResponse = {
  productName: string
  status: string
  releaseChannel: string
  uptime: string
  metrics: Metric[]
  workspaces: Workspace[]
  timeline: TimelineItem[]
}

export type FeedbackResponse = {
  status: string
  message: string
  preview: string
}

export type DeploymentNote = {
  title: string
  state: string
  description: string
}

export type WorkspaceDetail = {
  name: string
  region: string
  status: string
  members: number
  healthScore: number
  owner: string
  modules: string[]
  notes: DeploymentNote[]
}

export type WorkspaceDetailResponse = {
  items: WorkspaceDetail[]
}

export const workspaceSlug = (name: string) => name.trim().toLowerCase().replaceAll(' ', '-')
