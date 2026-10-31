import React from 'react';
import { useCurrentFrame } from 'remotion';
import { Fluent } from '../../../src/components/Icon';
import { WinFrame } from '../../../src/components/KdWindow';
import { Mono } from '../../../src/components/Keys';
import { Sfx } from '../../../src/components/Sfx';
import { ease, OUT } from '../../../src/lib/anim';
import { C, MONO } from '../../../src/theme';

/** A line of the terminal from frame `at`: a command (typed) or what came out (all at once). */
export type Line = { at: number; cmd?: string; out?: string; color?: string };

/** A terminal (PowerShell) whose commands are typed as their frames come. */
export const Terminal: React.FC<{ lines: Line[]; width?: number; height?: number; style?: React.CSSProperties; title?: string }> = ({ lines, width = 1100, height = 380, style, title = 'PowerShell' }) => {
  const f = useCurrentFrame();
  return (
    <WinFrame width={width} height={height} title={title} bar={40} bg="#0c0c10" style={style} icon={<Fluent name="window_console_20_filled" size={18} color={C.sky} />}>
      <div style={{ padding: '18px 24px', display: 'flex', flexDirection: 'column', gap: 10 }}>
        {lines.filter(l => f >= l.at).map((l, i) => {
          if (l.out !== undefined) return <Mono key={i} size={24} color={l.color ?? C.dim}>{l.out}</Mono>;
          const typed = Math.min(l.cmd!.length, Math.floor((f - l.at) * 1.6));
          const caret = typed < l.cmd!.length || (i === lines.filter(x => f >= x.at).length - 1 && f % 30 < 15);
          return (
            <Mono key={i} size={26} color={C.text}>
              <span style={{ color: C.accentText }}>PS&gt; </span>{l.cmd!.slice(0, typed)}{caret && <span>▍</span>}
            </Mono>
          );
        })}
      </div>
      {lines.filter(l => l.cmd).map(l => <Sfx key={l.at} at={l.at} name="key" volume={0.18} />)}
    </WinFrame>
  );
};

const KEYWORDS = /^(public|sealed|class|private|void|return|new|async|await|if|var|bool|true|false|null|double|is|or)$/;

/** Colours a line of C# or JSON: comments, strings, keywords and the rest. */
const Paint: React.FC<{ text: string }> = ({ text }) => {
  const parts = text.split(/(\/\/.*$|"[^"]*"|\b[a-z]+\b)/);
  return (
    <>
      {parts.map((p, i) => {
        const color = p.startsWith('//') ? '#6A9955' : p.startsWith('"') ? '#CE9178' : KEYWORDS.test(p) ? '#569CD6' : undefined;
        return <span key={i} style={color ? { color } : undefined}>{p}</span>;
      })}
    </>
  );
};

/** Lines (0-based, inclusive) lit from `from` to `to`, with an optional note beside them. */
export type Lit = { from: number; to: number; lines: [number, number] };

/** A code file in an editor window, with lines that light up while they are named. */
export const Code: React.FC<{ file: string; lines: string[]; lit?: Lit[]; width?: number; size?: number; style?: React.CSSProperties }> = ({ file, lines, lit = [], width = 1100, size = 24, style }) => {
  const f = useCurrentFrame();
  const row = size * 1.55;
  return (
    <WinFrame width={width} height={lines.length * row + 40 + 36} title={file} bar={40} bg="#1e1e1e" style={style} icon={<Fluent name="code_24_regular" size={18} color={C.violet} />}>
      <div style={{ position: 'relative', padding: '18px 0' }}>
        {lit.map((l, i) => {
          const k = Math.min(ease(f, l.from - 4, l.from + 8, OUT), 1 - ease(f, l.to - 8, l.to));
          if (k <= 0) return null;
          return <div key={i} style={{ position: 'absolute', left: 0, right: 0, top: 18 + l.lines[0] * row, height: (l.lines[1] - l.lines[0] + 1) * row, background: `rgba(242,193,74,${0.16 * k})`, borderLeft: `4px solid rgba(242,193,74,${k})` }} />;
        })}
        {lines.map((l, i) => (
          <div key={i} style={{ position: 'relative', height: row, display: 'flex', alignItems: 'center', fontFamily: MONO, fontSize: size, color: '#D4D4D4', whiteSpace: 'pre' }}>
            <span style={{ width: 56, textAlign: 'right', paddingRight: 18, color: '#6e7681' }}>{i + 1}</span>
            <Paint text={l} />
          </div>
        ))}
      </div>
    </WinFrame>
  );
};
