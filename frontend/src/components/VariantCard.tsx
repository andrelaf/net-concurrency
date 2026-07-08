import { useState } from 'react'
import type { RunVariant } from '../api'

// Renders the outcome of one variant (antipattern or pattern): elapsed time
// bar, headline, metric tiles, and a collapsible execution log.
export function VariantCard({
  variant,
  maxElapsed,
}: {
  variant: RunVariant
  maxElapsed: number
}) {
  const [showLog, setShowLog] = useState(false)
  const isAnti = variant.kind === 'antipattern'
  const barPct = maxElapsed > 0 ? Math.max(4, (variant.elapsedMs / maxElapsed) * 100) : 0

  return (
    <div className={`variant-card ${isAnti ? 'is-anti' : 'is-pattern'}`}>
      <div className="variant-head">
        <span className="variant-icon">{isAnti ? '❌' : '✅'}</span>
        <span className="variant-title">{isAnti ? 'Antipadrão' : 'Padrão'}</span>
        <span className={`ok-badge ${variant.ok ? 'ok-good' : 'ok-bad'}`}>
          {variant.ok ? 'correto' : 'defeituoso'}
        </span>
        <span className="elapsed">{variant.elapsedMs.toLocaleString()} ms</span>
      </div>

      <div className="bar-track" title={`${variant.elapsedMs} ms`}>
        <div className={`bar-fill ${isAnti ? 'fill-anti' : 'fill-pattern'}`} style={{ width: `${barPct}%` }} />
      </div>

      <p className="variant-headline">{variant.headline}</p>

      <div className="metric-grid">
        {variant.metrics.map((m) => (
          <div className="metric-tile" key={m.label}>
            <div className="metric-value">{m.value}</div>
            <div className="metric-label">{m.label}</div>
            {m.hint && <div className="metric-hint">{m.hint}</div>}
          </div>
        ))}
      </div>

      {variant.log.length > 0 && (
        <div className="log-wrap">
          <button className="log-toggle" onClick={() => setShowLog((s) => !s)}>
            {showLog ? '▾' : '▸'} log de execução ({variant.log.length})
          </button>
          {showLog && (
            <pre className="log-body">
              {variant.log.map((l, i) => (
                <div key={i}>{l}</div>
              ))}
            </pre>
          )}
        </div>
      )}
    </div>
  )
}
