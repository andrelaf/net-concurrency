// Types mirror the C# records in backend/ConcurrencyLab.Api/Demos.
// System.Text.Json serializes them as camelCase.

export interface DemoParameter {
  name: string
  label: string
  default: number
  min: number
  max: number
  step: number
  hint?: string | null
}

export interface DemoInfo {
  id: string
  title: string
  category: string
  summary: string
  antipatternCode: string
  antipatternExplanation: string
  patternCode: string
  patternExplanation: string
  keyTakeaways: string[]
  supportsRun: boolean
  parameters: DemoParameter[]
  chapter?: string | null
}

export interface MetricItem {
  label: string
  value: string
  hint?: string | null
}

export interface RunVariant {
  kind: 'antipattern' | 'pattern'
  ok: boolean
  elapsedMs: number
  headline: string
  metrics: MetricItem[]
  log: string[]
}

export interface RunResponse {
  demoId: string
  antipattern: RunVariant | null
  pattern: RunVariant | null
  verdict: string
}

async function json<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const body = await res.text()
    throw new Error(`${res.status} ${res.statusText}${body ? ` — ${body}` : ''}`)
  }
  return res.json() as Promise<T>
}

export const api = {
  demos: () => fetch('/api/demos').then(json<DemoInfo[]>),

  run: (id: string, parameters: Record<string, number>) =>
    fetch(`/api/demos/${id}/run`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ variant: 'both', parameters }),
    }).then(json<RunResponse>),
}
