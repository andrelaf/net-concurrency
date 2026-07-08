import { useEffect, useMemo, useState } from 'react'
import { api, type DemoInfo } from './api'
import { DemoDetail } from './components/DemoDetail'
import './App.css'

export default function App() {
  const [demos, setDemos] = useState<DemoInfo[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [loadError, setLoadError] = useState('')

  useEffect(() => {
    api
      .demos()
      .then((d) => {
        setDemos(d)
        if (d.length) setSelectedId((cur) => cur ?? d[0].id)
      })
      .catch((e) => setLoadError(e instanceof Error ? e.message : String(e)))
  }, [])

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return demos
    return demos.filter(
      (d) =>
        d.title.toLowerCase().includes(q) ||
        d.summary.toLowerCase().includes(q) ||
        d.category.toLowerCase().includes(q),
    )
  }, [demos, query])

  const groups = useMemo(() => {
    const map = new Map<string, DemoInfo[]>()
    for (const d of filtered) {
      const list = map.get(d.category) ?? []
      list.push(d)
      map.set(d.category, list)
    }
    return [...map.entries()]
  }, [filtered])

  const selected = demos.find((d) => d.id === selectedId) ?? null

  return (
    <div className="app">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark">⚡</div>
          <div>
            <div className="brand-title">ConcurrencyLab</div>
            <div className="brand-sub">.NET 10 · padrões e antipadrões</div>
          </div>
        </div>

        <input
          className="search"
          placeholder="Buscar demos…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />

        <nav className="nav">
          {groups.map(([cat, items]) => (
            <div className="nav-group" key={cat}>
              <div className="nav-cat">{cat}</div>
              {items.map((d) => (
                <button
                  key={d.id}
                  className={`nav-item ${d.id === selectedId ? 'active' : ''}`}
                  onClick={() => setSelectedId(d.id)}
                >
                  <span className="nav-item-title">{d.title}</span>
                  {!d.supportsRun && <span className="nav-tag">ficha</span>}
                </button>
              ))}
            </div>
          ))}
          {groups.length === 0 && <div className="nav-empty">Nenhuma demo corresponde a “{query}”.</div>}
        </nav>

        <div className="side-foot">
          {demos.length} demos · executadas ao vivo no ASP.NET&nbsp;Core
        </div>
      </aside>

      <main className="main">
        {loadError ? (
          <div className="fatal">
            <h2>Não foi possível conectar ao backend</h2>
            <p>{loadError}</p>
            <p className="fatal-hint">
              Inicie com <code>dotnet run</code> em{' '}
              <code>backend/ConcurrencyLab.Api</code> (esperado em
              <code> http://localhost:5180</code>).
            </p>
          </div>
        ) : selected ? (
          <DemoDetail demo={selected} />
        ) : (
          <div className="loading">Carregando demos…</div>
        )}
      </main>
    </div>
  )
}
