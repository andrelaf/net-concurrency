import { useEffect, useState } from 'react'
import { api, type DemoInfo, type RunResponse } from '../api'
import { VariantCard } from './VariantCard'

type Status = 'idle' | 'running' | 'done' | 'error'

// Parameter sliders + Run button + side-by-side results for a runnable demo.
export function RunPanel({ demo }: { demo: DemoInfo }) {
  const [params, setParams] = useState<Record<string, number>>({})
  const [status, setStatus] = useState<Status>('idle')
  const [result, setResult] = useState<RunResponse | null>(null)
  const [error, setError] = useState<string>('')

  // Reset parameters and results whenever the selected demo changes.
  useEffect(() => {
    const init: Record<string, number> = {}
    for (const p of demo.parameters) init[p.name] = p.default
    setParams(init)
    setStatus('idle')
    setResult(null)
    setError('')
  }, [demo.id])

  async function run() {
    setStatus('running')
    setError('')
    try {
      const res = await api.run(demo.id, params)
      setResult(res)
      setStatus('done')
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setStatus('error')
    }
  }

  const maxElapsed = Math.max(
    result?.antipattern?.elapsedMs ?? 0,
    result?.pattern?.elapsedMs ?? 0,
    1,
  )

  return (
    <section className="run-panel">
      <div className="run-header">
        <h3>Execute ao vivo</h3>
        <p className="run-sub">
          Executa as duas variantes no backend .NET&nbsp;10 e reporta medições reais.
        </p>
      </div>

      <div className="params">
        {demo.parameters.map((p) => (
          <label className="param" key={p.name}>
            <div className="param-top">
              <span className="param-label">{p.label}</span>
              <span className="param-value">{(params[p.name] ?? p.default).toLocaleString()}</span>
            </div>
            <input
              type="range"
              min={p.min}
              max={p.max}
              step={p.step}
              value={params[p.name] ?? p.default}
              disabled={status === 'running'}
              onChange={(e) =>
                setParams((prev) => ({ ...prev, [p.name]: Number(e.target.value) }))
              }
            />
            {p.hint && <span className="param-hint">{p.hint}</span>}
          </label>
        ))}
      </div>

      <button className="run-btn" onClick={run} disabled={status === 'running'}>
        {status === 'running' ? (
          <>
            <span className="spinner" /> Executando na CPU…
          </>
        ) : (
          <>▶ Executar as duas variantes</>
        )}
      </button>

      {status === 'error' && <div className="run-error">Falha na execução: {error}</div>}

      {result && (
        <div className="results">
          {result.verdict && <div className="verdict">{result.verdict}</div>}
          <div className="variant-grid">
            {result.antipattern && (
              <VariantCard variant={result.antipattern} maxElapsed={maxElapsed} />
            )}
            {result.pattern && (
              <VariantCard variant={result.pattern} maxElapsed={maxElapsed} />
            )}
          </div>
        </div>
      )}
    </section>
  )
}
