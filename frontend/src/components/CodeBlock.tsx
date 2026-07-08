import { type JSX, useMemo } from 'react'

// A tiny, dependency-free C# highlighter. It is intentionally approximate:
// good enough to make the demo snippets readable without pulling in a full
// tokenizer/grammar. Renders spans (no dangerouslySetInnerHTML).

const KEYWORDS = new Set([
  'abstract', 'as', 'async', 'await', 'base', 'bool', 'break', 'byte', 'case',
  'catch', 'char', 'class', 'const', 'continue', 'decimal', 'default', 'do',
  'double', 'else', 'enum', 'false', 'finally', 'float', 'for', 'foreach',
  'get', 'if', 'in', 'int', 'interface', 'internal', 'is', 'lock', 'long',
  'namespace', 'new', 'null', 'object', 'out', 'override', 'params', 'private',
  'protected', 'public', 'readonly', 'record', 'ref', 'return', 'sealed',
  'set', 'short', 'static', 'string', 'struct', 'switch', 'this', 'throw',
  'true', 'try', 'typeof', 'using', 'var', 'virtual', 'void', 'while', 'yield',
])

type Tok = { text: string; cls: string }

function tokenizeLine(line: string): Tok[] {
  const toks: Tok[] = []
  let i = 0
  const n = line.length
  while (i < n) {
    const c = line[i]

    // line comment -> rest of line
    if (c === '/' && line[i + 1] === '/') {
      toks.push({ text: line.slice(i), cls: 'tok-comment' })
      break
    }
    // string literal
    if (c === '"') {
      let j = i + 1
      while (j < n && line[j] !== '"') {
        if (line[j] === '\\') j++
        j++
      }
      j = Math.min(j + 1, n)
      toks.push({ text: line.slice(i, j), cls: 'tok-string' })
      i = j
      continue
    }
    // identifier / keyword
    if (/[A-Za-z_]/.test(c)) {
      let j = i + 1
      while (j < n && /[A-Za-z0-9_]/.test(line[j])) j++
      const word = line.slice(i, j)
      if (KEYWORDS.has(word)) toks.push({ text: word, cls: 'tok-kw' })
      else if (/^[A-Z]/.test(word)) toks.push({ text: word, cls: 'tok-type' })
      else toks.push({ text: word, cls: 'tok-id' })
      i = j
      continue
    }
    // number
    if (/[0-9]/.test(c)) {
      let j = i + 1
      while (j < n && /[0-9_.a-fx]/i.test(line[j])) j++
      toks.push({ text: line.slice(i, j), cls: 'tok-num' })
      i = j
      continue
    }
    // punctuation / whitespace
    toks.push({ text: c, cls: 'tok-punct' })
    i++
  }
  return toks
}

export function CodeBlock({ code }: { code: string }) {
  const lines = useMemo(() => code.replace(/\r\n/g, '\n').split('\n'), [code])
  return (
    <pre className="code-block" aria-label="source code">
      <code>
        {lines.map((line, li) => {
          const toks = tokenizeLine(line)
          const isComment = line.trimStart().startsWith('//')
          return (
            <span className="code-line" key={li}>
              {toks.length === 0 ? (
                '​'
              ) : (
                toks.map((t, ti): JSX.Element => (
                  <span key={ti} className={isComment ? 'tok-comment' : t.cls}>
                    {t.text}
                  </span>
                ))
              )}
              {'\n'}
            </span>
          )
        })}
      </code>
    </pre>
  )
}
