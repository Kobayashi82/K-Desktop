import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../../../src/components/Chapter';
import { Fluent } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { glide, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

/** «Other servers», drawn with its real texts: a server added, tested, and its models in the list. */
const Other: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const o = B('other');
  const chips = [{ cue: 'ollama', t: t('Ollama') }, { cue: 'swap', t: 'llama-swap' }, { cue: 'lmstudio', t: t('LM Studio') }];
  const card = glide(f, o.cue('other') - 16, 22);
  const ok = pop(f, o.cue('other') + 16, { damping: 14 });
  const models = [{ n: 'llama3.3:70b', s: t('42 GB · Ollama') }, { n: 'qwen3:32b', s: t('20 GB · Ollama') }];
  const box = (label: string, value: string, w: number) => (
    <div style={{ width: w }}>
      <div style={{ fontSize: 18, color: C.dim, marginBottom: 6 }}>{label}</div>
      <div style={{ height: 50, display: 'flex', alignItems: 'center', padding: '0 16px', borderRadius: 8, background: 'rgba(255,255,255,0.06)', border: `1px solid ${C.line2}`, fontSize: 21, color: C.text }}>{value}</div>
    </div>
  );
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 150, display: 'flex', justifyContent: 'center', gap: 22 }}>
        {chips.map(c => {
          const k = pop(f, o.cue(c.cue) - 6, { damping: 13 });
          return <span key={c.cue} style={{ fontSize: 34, fontWeight: 800, padding: '14px 30px', borderRadius: 30, background: 'rgba(28,28,34,0.97)', border: `1.5px solid ${C.line2}`, color: C.text, opacity: Math.min(1, k * 1.4), transform: `scale(${k})` }}>{c.t}</span>;
        })}
      </div>
      <div style={{
        position: 'absolute', left: 960, top: 300, width: 1100, padding: '28px 34px', borderRadius: 26, boxSizing: 'border-box', transform: `translateX(-50%) translateY(${(1 - card) * 60}px)`, opacity: card,
        background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
      }}>
        <div style={{ fontSize: 30, fontWeight: 700, color: C.text }}>{t('Other servers')}</div>
        <div style={{ fontSize: 20, color: C.dim, marginTop: 6, marginBottom: 22 }}>{t('Ollama, llama-swap, LM Studio… on this PC or another. Their models show up in the list above.')}</div>
        <div style={{ display: 'flex', alignItems: 'flex-end', gap: 16 }}>
          {box(t('Server type'), t('Ollama'), 220)}
          {box(t('Address'), 'http://192.168.1.20:11434', 520)}
          <div style={{ height: 50, padding: '0 26px', display: 'flex', alignItems: 'center', borderRadius: 8, background: C.accentText, color: '#1a1306', fontSize: 21, fontWeight: 600 }}>{t('Add')}</div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 18, fontSize: 21, color: C.success, opacity: Math.min(1, ok * 1.4) }}>
          <Fluent name="checkmark_circle_24_filled" size={26} color={C.success} /> {t('Connected: 14 models (38 ms)')}
        </div>
        <div style={{ height: 1, background: C.line2, margin: '20px 0 14px' }} />
        {models.map((m, i) => {
          const k = pop(f, o.cue('list') - 4 + i * 6, { damping: 14 });
          return (
            <div key={m.n} style={{ display: 'flex', alignItems: 'center', gap: 16, height: 58, padding: '0 14px', borderRadius: 10, marginBottom: 6, background: 'rgba(255,255,255,0.05)', opacity: Math.min(1, k * 1.4), transform: `translateY(${(1 - k) * 20}px)` }}>
              <Fluent name="cube_24_regular" size={26} color={C.accentText} />
              <span style={{ fontSize: 23, fontWeight: 600, color: C.text }}>{m.n}</span>
              <span style={{ fontSize: 19, color: C.dim }}>{m.s}</span>
            </div>
          );
        })}
      </div>
      {chips.map(c => <Sfx key={c.cue} at={o.cue(c.cue) - 6} name="pop" volume={0.18} />)}
      <Sfx at={o.cue('other') + 16} name="chime" volume={0.18} />
    </AbsoluteFill>
  );
};

/** Where your words go: the three places the brain options name. */
const Where: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const w = B('where');
  const places = [
    { cue: 'thispc', icon: 'desktop_24_filled', color: C.success, title: t('This PC'), sub: t('On this PC: what you say never leaves the computer.') },
    { cue: 'network', icon: 'home_24_filled', color: C.sky, title: t('Your network'), sub: t('On another computer of your network (192.168.1.20): what you say stays at home.') },
    { cue: 'internet', icon: 'globe_24_filled', color: C.warning, title: t('The internet'), sub: t('On the internet (api.example.com): what you say leaves your network.') },
  ];
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 300, display: 'flex', justifyContent: 'center', gap: 34 }}>
        {places.map(p => {
          const k = pop(f, w.cue(p.cue) - 6);
          return (
            <div key={p.cue} style={{ width: 470, padding: 32, borderRadius: 26, boxSizing: 'border-box', background: 'rgba(32,32,38,0.97)', border: `2px solid ${p.color}`, opacity: Math.min(1, k * 1.4), transform: `translateY(${(1 - k) * 60}px)` }}>
              <Fluent name={p.icon} size={64} color={p.color} />
              <div style={{ fontFamily: SANS, fontSize: 38, fontWeight: 800, color: C.text, marginTop: 14 }}>{p.title}</div>
              <div style={{ fontFamily: SANS, fontSize: 23, color: C.dim, marginTop: 10, lineHeight: 1.4 }}>{p.sub}</div>
            </div>
          );
        })}
      </div>
      {places.map(p => <Sfx key={p.cue} at={w.cue(p.cue) - 6} name="pop" volume={0.2} />)}
    </AbsoluteFill>
  );
};

/** Chapter «Other servers». */
export const Servers: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const w = B('where');
  return (
    <AbsoluteFill>
      <Part from={0} to={w.from - 4} first><Other /></Part>
      <Part from={w.from - 4} to={durationInFrames} last><Where /></Part>
    </AbsoluteFill>
  );
};
