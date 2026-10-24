import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { CaptureShot } from '../../components/Capture';
import { Fluent, KdIcon } from '../../components/Icon';
import { Keycap, Mono } from '../../components/Keys';
import { Sfx } from '../../components/Sfx';
import { ease, glide, inOut, OUT, pop } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, SANS } from '../../theme';

export const Bubble: React.FC<{ text: React.ReactNode; show: number; x: number; y: number; user?: boolean; size?: number; width?: number }> = ({ text, show, x, y, user, size = 34, width }) => (
  <div style={{
    position: 'absolute', left: x, top: y, width, transform: `translate(${user ? '-100%' : '0'}, -50%) scale(${0.8 + 0.2 * show})`, transformOrigin: user ? 'right center' : 'left center', opacity: show,
    padding: `${size * 0.5}px ${size * 0.8}px`, borderRadius: size * 0.9, fontFamily: SANS, fontSize: size, fontWeight: 700, lineHeight: 1.3,
    background: user ? C.accentText : 'white', color: user ? '#1a1306' : '#141418', boxShadow: '0 20px 50px rgba(0,0,0,0.5)', whiteSpace: width ? 'normal' : 'nowrap',
  }}>{text}</div>
);

const Lander: React.FC<{ x: number; y: number; size?: number; style?: React.CSSProperties }> = ({ x, y, size = 220, style }) => {
  const f = useCurrentFrame();
  return <Img src={staticFile('brand/lander/classic.png')} style={{ position: 'absolute', left: x - size / 2, top: y - size / 2 + Math.sin(f / 14) * 8, width: size, height: size, ...style }} />;
};

/** Its own process, a transparent layer, windows as terrain. */
export const ArchPart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const layers = glide(f, b.cue('layer') - 10, 36);
  const terrain = ease(f, b.cue('terrain') - 6, b.cue('terrain') + 10);
  const procShow = 1 - layers;
  const tilt = 52 * layers;
  return (
    <AbsoluteFill>
      {/* Two processes */}
      <div style={{ position: 'absolute', left: 0, right: 0, top: 380, display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 60, opacity: procShow }}>
        {[
          { at: b.from, icon: 'shield_checkmark_24_filled', color: 'gold', title: 'K-Desktop', sub: 'Administrator' },
          { at: b.cue('process') - 6, icon: 'person_24_filled', color: 'purple', title: 'Assistant process', sub: 'Normal user · no admin rights' },
        ].map((p, i) => {
          const k = pop(f, p.at - 4, { damping: 13 });
          return (
            <React.Fragment key={p.title}>
              {i === 1 && <div style={{ width: 160 * ease(f, p.at, p.at + 14), height: 4, background: C.accentText, borderRadius: 2 }} />}
              <div style={{ display: 'flex', alignItems: 'center', gap: 20, padding: '26px 34px', borderRadius: 26, background: 'rgba(32,32,38,0.97)', border: `1.5px solid ${i === 1 && f > b.cue('admin') - 4 ? 'rgba(242,193,74,0.7)' : C.line2}`, opacity: k, transform: `scale(${0.8 + 0.2 * k})` }}>
                {i === 0 ? <Img src={staticFile('brand/icon-192.png')} style={{ width: 80, height: 80 }} /> : <Img src={staticFile('brand/lander/icon.png')} style={{ width: 80, height: 80 }} />}
                <div>
                  <div style={{ fontFamily: SANS, fontSize: 34, fontWeight: 800, color: C.text }}>{p.title}</div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontFamily: SANS, fontSize: 22, color: C.dim }}><Fluent name={p.icon} size={22} color={C.accentText} />{p.sub}</div>
                </div>
              </div>
            </React.Fragment>
          );
        })}
      </div>
      {/* The layers */}
      <AbsoluteFill style={{ perspective: 2200, opacity: layers }}>
        <div style={{ position: 'absolute', left: 560, top: 230, width: 800, height: 500, transformStyle: 'preserve-3d', transform: `rotateX(${tilt}deg) rotateZ(${-22 * layers}deg)` }}>
          <div style={{ position: 'absolute', inset: 0, borderRadius: 16, background: 'linear-gradient(135deg,#1c2a5a,#3a1f6a)', transform: 'translateZ(0px)' }} />
          {[{ x: 60, y: 70, w: 420, h: 300 }, { x: 360, y: 180, w: 380, h: 260 }].map((w, i) => (
            <div key={i} style={{ position: 'absolute', left: w.x, top: w.y, width: w.w, height: w.h, borderRadius: 10, background: '#25262d', border: '1px solid rgba(255,255,255,0.15)', transform: `translateZ(${80 * layers}px)` }}>
              <div style={{ height: 26, borderRadius: '10px 10px 0 0', background: terrain > 0 ? `rgba(242,193,74,${0.25 + 0.6 * terrain})` : '#30313a', boxShadow: terrain > 0 ? `0 0 ${30 * terrain}px rgba(242,193,74,0.7)` : 'none' }} />
            </div>
          ))}
          <div style={{ position: 'absolute', inset: -20, borderRadius: 20, border: `2px dashed rgba(85,210,255,0.7)`, background: 'rgba(85,210,255,0.06)', transform: `translateZ(${170 * layers}px)` }}>
            <Img src={staticFile('brand/lander/classic.png')} style={{ position: 'absolute', left: 120 + 60 * terrain, top: 20 + 40 * terrain, width: 110, height: 110, transform: 'rotateX(-50deg)', transformOrigin: 'bottom center' }} />
          </div>
        </div>
        <div style={{ position: 'absolute', left: 1380, top: 300, display: 'flex', flexDirection: 'column', gap: 120, opacity: ease(f, b.cue('layer'), b.cue('layer') + 20) }}>
          <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.sky }}>Transparent layer · GPU</div>
          <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: terrain > 0.5 ? C.accentText : C.text }}>{terrain > 0.5 ? 'Your windows = terrain' : 'Your windows'}</div>
        </div>
      </AbsoluteFill>
      <Sfx at={b.cue('process') - 6} name="pop" volume={0.25} />
      <Sfx at={b.cue('layer') - 10} name="whoosh" volume={0.25} />
      <Sfx at={b.cue('terrain') - 6} name="chime" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Talking to them: their names, a key or a click, and answering back. */
export const TalkPart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const ownAt = b.cue('own');
  const showWords = inOut(f, ownAt - 8, b.cue('key') - 6, 10);
  const lastPart = b.cue('key') - 6;
  return (
    <AbsoluteFill>
      {/* «Lander, what time is it?» */}
      <AbsoluteFill style={{ opacity: 1 - showWords * (f < lastPart ? 1 : 0) }}>
        {f < ownAt && (
          <>
            <Lander x={1260} y={560} size={300} />
            <Bubble user text="Lander, what time is it?" show={pop(f, b.cue('question') - 6)} x={1080} y={360} size={40} />
            <Bubble text="12:52, Saturday. Over." show={pop(f, b.cue('question') + 34)} x={1400} y={700} size={36} />
          </>
        )}
      </AbsoluteFill>
      {/* Each its own word */}
      <AbsoluteFill style={{ opacity: showWords }}>
        <CaptureShot name="settings-voice"
          shots={[{ at: ownAt - 8, x: 1040, y: 790, s: 1.5 }, { at: ownAt + 10, dur: 60, x: 1040, y: 790, s: 1.62 }]}
          focus={[
            { from: ownAt - 2, to: b.cue('kd') - 4, rect: [571, 704, 936, 152], label: 'Each assistant, its own word', at: 'top' },
            { from: b.cue('kd') - 4, to: b.cue('key'), rect: [571, 868, 936, 72], label: 'KDesktop → your commands', at: 'top' },
          ]} />
      </AbsoluteFill>
      {/* A key, a click, and just answering */}
      {f >= lastPart - 10 && (
        <AbsoluteFill style={{ opacity: ease(f, lastPart - 10, lastPart + 6) }}>
          <div style={{ position: 'absolute', left: 300, top: 360, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 20, opacity: pop(f, b.cue('key') - 6) }}>
            <Keycap label={<Fluent name="mic_24_filled" size={56} color={inOut(f, b.cue('key'), b.cue('click'), 4) > 0.5 ? '#1a1306' : C.text} />} height={150} width={170} down={inOut(f, b.cue('key'), b.cue('click'), 4)} />
            <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text }}>Hold its key</span>
          </div>
          <div style={{ position: 'absolute', left: 760, top: 300, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 20, opacity: pop(f, b.cue('click') - 6) }}>
            <div style={{ position: 'relative', width: 220, height: 220 }}>
              <Img src={staticFile('brand/lander/classic.png')} style={{ width: 220, height: 220 }} />
              <div style={{ position: 'absolute', left: 60, top: 60, width: 100, height: 100, borderRadius: 50, border: `5px solid ${C.accentText}`, opacity: inOut(f, b.cue('click'), b.cue('asks'), 4), transform: `scale(${0.8 + 0.2 * Math.sin(f / 4)})` }} />
            </div>
            <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text }}>Hold the click on it</span>
          </div>
          <div style={{ position: 'absolute', left: 1180, top: 280, width: 640, opacity: pop(f, b.cue('asks') - 6) }}>
            <Bubble text="Shall I remind you at 16:45?" show={pop(f, b.cue('asks') - 6)} x={0} y={60} size={30} />
            <Bubble user text="Yes, please" show={pop(f, b.cue('asks') + 14)} x={620} y={190} size={30} />
            <div style={{ position: 'absolute', left: 0, top: 270, fontFamily: SANS, fontSize: 24, color: C.accentText, fontWeight: 700, opacity: pop(f, b.cue('asks') + 24) }}>No need to call it again</div>
          </div>
        </AbsoluteFill>
      )}
      <Sfx at={b.cue('question') - 6} name="pop" volume={0.25} />
      <Sfx at={b.cue('question') + 34} name="blip" volume={0.25} />
      <Sfx at={b.cue('key')} name="key" volume={0.35} />
      <Sfx at={b.cue('click')} name="click" volume={0.3} />
      <Sfx at={b.cue('asks') + 14} name="pop" volume={0.2} />
    </AbsoluteFill>
  );
};

/** The brain: a language model on your PC (Settings › Brain), where your words go, and the fixed rules. */
export const BrainPart: React.FC<{ brain: BeatTimes; brain2: BeatTimes }> = ({ brain, brain2 }) => {
  const f = useCurrentFrame();
  const places = [
    { cue: 'thispc', icon: 'desktop_24_filled', color: C.success, title: 'This PC', sub: 'what you say never leaves it' },
    { cue: 'network', icon: 'home_24_filled', color: C.sky, title: 'Your network', sub: 'it stays at home' },
    { cue: 'internet', icon: 'globe_24_filled', color: C.warning, title: 'The internet', sub: 'it leaves your network' },
  ];
  const servers = [{ cue: 'llama', t: 'llama-server' }, { cue: 'ollama', t: 'Ollama' }, { cue: 'openai', t: 'OpenAI compatible' }];
  const rules = [
    { cue: 'time', q: 'What time is it?', a: '12:52' },
    { cue: 'timer', q: 'A timer for 5 minutes', a: 'Timer set' },
    { cue: 'note', q: 'Note: buy batteries', a: 'Noted' },
    { cue: 'cancel', q: 'Cancel', a: 'Cancelled' },
  ];
  const rulesShow = inOut(f, brain2.cue('time') - 10, brain2.to + 40, 10);
  const placesShow = inOut(f, brain.cue('where') - 8, brain2.from + 4, 10);
  return (
    <AbsoluteFill>
      <CaptureShot name="brain"
        shots={[
          { at: 0, x: 960, y: 420, s: 1.15 },
          { at: brain.cue('pc') - 8, dur: 30, x: 1040, y: 230, s: 1.55 },
          { at: brain.cue('llama') - 8, dur: 30, x: 1040, y: 760, s: 1.45 },
          { at: brain.cue('biggest') - 8, dur: 30, x: 1040, y: 260, s: 1.5 },
          { at: brain2.cue('unloads') - 8, dur: 30, x: 1040, y: 520, s: 1.55 },
        ]}
        focus={[
          { from: brain.cue('pc') - 4, to: brain.cue('llama') - 4, rect: [572, 157, 937, 46], label: 'It loads by itself when you talk' },
          { from: brain.cue('llama') - 4, to: brain.cue('biggest') - 4, rect: [572, 700, 937, 210], label: 'GGUF models from Hugging Face', at: 'top' },
          { from: brain.cue('biggest') - 4, to: brain.cue('where') - 8, rect: [572, 218, 937, 44], label: 'Automatic: the biggest that fits your GPU' },
          { from: brain2.cue('unloads') - 4, to: brain2.cue('games') - 4, rect: [572, 483, 937, 42], label: 'Unloads when idle' },
          { from: brain2.cue('games') - 4, to: brain2.cue('time') - 10, rect: [572, 533, 937, 42], label: 'Free the graphics card for games' },
        ]} />
      {/* Which servers */}
      <div style={{ position: 'absolute', left: 0, right: 0, top: 150, display: 'flex', justifyContent: 'center', gap: 20, opacity: inOut(f, brain.cue('ollama') - 8, brain.cue('where') - 8, 8) }}>
        {servers.map(s => (
          <span key={s.cue} style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, padding: '14px 26px', borderRadius: 30, background: 'rgba(28,28,34,0.97)', border: `1.5px solid ${C.line2}`, color: C.text, opacity: pop(f, Math.max(brain.cue('ollama') - 8, brain.cue(s.cue) - 6)) }}>{s.t}</span>
        ))}
      </div>
      {/* Where your words go */}
      <AbsoluteFill style={{ background: `rgba(8,8,12,${0.7 * placesShow})` }}>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 360, display: 'flex', justifyContent: 'center', gap: 34, opacity: placesShow }}>
          {places.map(p => {
            const k = pop(f, brain.cue(p.cue) - 6);
            return (
              <div key={p.cue} style={{ width: 430, padding: 30, borderRadius: 26, background: 'rgba(32,32,38,0.97)', border: `2px solid ${p.color}`, opacity: k, transform: `translateY(${(1 - k) * 60}px)` }}>
                <Fluent name={p.icon} size={60} color={p.color} />
                <div style={{ fontFamily: SANS, fontSize: 36, fontWeight: 800, color: C.text, marginTop: 14 }}>{p.title}</div>
                <div style={{ fontFamily: SANS, fontSize: 22, color: C.dim, marginTop: 6 }}>{p.sub}</div>
              </div>
            );
          })}
        </div>
      </AbsoluteFill>
      {/* Fixed rules */}
      <AbsoluteFill style={{ background: `rgba(8,8,12,${0.75 * rulesShow})` }}>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 250, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 22, opacity: rulesShow }}>
          {rules.map(r => {
            const k = pop(f, brain2.cue(r.cue) - 6);
            return (
              <div key={r.cue} style={{ display: 'flex', alignItems: 'center', gap: 20, opacity: k, transform: `translateX(${(1 - k) * -80}px)` }}>
                <span style={{ width: 460, textAlign: 'right', fontFamily: SANS, fontSize: 32, fontWeight: 800, color: C.text }}>“{r.q}”</span>
                <Fluent name="flash_24_filled" size={36} color={C.accentText} />
                <span style={{ width: 280, fontFamily: SANS, fontSize: 30, fontWeight: 700, color: C.success }}>{r.a}</span>
              </div>
            );
          })}
          <div style={{ marginTop: 20, fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.accentText, opacity: pop(f, brain2.cue('rules') - 6) }}>Instant · no AI needed</div>
        </div>
      </AbsoluteFill>
      {places.map(p => <Sfx key={p.cue} at={brain.cue(p.cue) - 6} name="pop" volume={0.2} />)}
      {rules.map(r => <Sfx key={r.cue} at={brain2.cue(r.cue) - 6} name="blip" volume={0.16} />)}
    </AbsoluteFill>
  );
};

export { KdIcon, Mono, OUT };
