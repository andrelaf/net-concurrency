import type { DemoInfo } from '../api'
import { CodeBlock } from './CodeBlock'
import { RunPanel } from './RunPanel'

// The main reading surface: summary, antipattern vs pattern code + notes,
// key takeaways, and (when runnable) the live run panel.
export function DemoDetail({ demo }: { demo: DemoInfo }) {
  return (
    <article className="detail">
      <header className="detail-head">
        <div className="chips">
          <span className="chip chip-cat">{demo.category}</span>
          <span className={`chip ${demo.supportsRun ? 'chip-run' : 'chip-note'}`}>
            {demo.supportsRun ? 'executável' : 'ficha de estudo'}
          </span>
          {demo.chapter && <span className="chip chip-book">📖 {demo.chapter}</span>}
          {demo.since && <span className="chip chip-since">⬆ {demo.since}</span>}
        </div>
        <h1>{demo.title}</h1>
        <p className="detail-summary">{demo.summary}</p>
      </header>

      <div className="compare">
        <div className="compare-col col-anti">
          <div className="compare-head">
            <span>❌</span> Antipadrão
          </div>
          <CodeBlock code={demo.antipatternCode} />
          <p className="explain">{demo.antipatternExplanation}</p>
        </div>

        <div className="compare-col col-pattern">
          <div className="compare-head">
            <span>✅</span> Padrão
          </div>
          <CodeBlock code={demo.patternCode} />
          <p className="explain">{demo.patternExplanation}</p>
        </div>
      </div>

      {demo.useCases && demo.useCases.length > 0 && (
        <section className="usecases">
          <h3>🎯 Quando usar (casos de uso)</h3>
          <ul>
            {demo.useCases.map((u, i) => (
              <li key={i}>{u}</li>
            ))}
          </ul>
        </section>
      )}

      <section className="takeaways">
        <h3>Pontos-chave</h3>
        <ul>
          {demo.keyTakeaways.map((t, i) => (
            <li key={i}>{t}</li>
          ))}
        </ul>
      </section>

      {demo.supportsRun ? (
        <RunPanel demo={demo} />
      ) : (
        <div className="study-note">
          Este risco é inseguro ou não determinístico para rodar em um servidor compartilhado (pode
          dar deadlock ou derrubar o processo), então é apresentado como ficha de estudo. O código
          acima mostra exatamente como ele falha e como corrigir.
        </div>
      )}
    </article>
  )
}
