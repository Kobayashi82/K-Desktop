import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { FAMILIES } from '../../components/ActionFamilies';
import { CaptureShot } from '../../components/Capture';
import { Fluent, KdIcon } from '../../components/Icon';
import { Mono } from '../../components/Keys';
import { Sfx } from '../../components/Sfx';
import { ease, glide, inOut, pop } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, SANS } from '../../theme';
import { Bubble } from './Inside';
import { PipSprite } from './Meet';

/** What they remember about you (Settings › Memory). */
export const MemoryPart: React.FC<{ b: BeatTimes }> = ({ b }) => (
  <CaptureShot name="memory"
    shots={[{ at: 0, x: 900, y: 380, s: 1.45 }, { at: b.cue('see') - 10, dur: 40, x: 880, y: 440, s: 1.6 }]}
    focus={[
      { from: b.cue('remember') - 4, to: b.cue('see') - 4, rect: [567, 162, 594, 42], label: 'Learn from what you talk about' },
      { from: b.cue('see') - 4, to: b.to + 30, rect: [567, 266, 594, 346], label: 'See and edit what they know', at: 'right' },
    ]} />
);

/** Your documents (Settings › What it knows), and an answer with its source. */
export const KnowledgePart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const chat = inOut(f, b.cue('ask') - 10, b.to + 40, 10);
  return (
    <AbsoluteFill>
      <CaptureShot name="knowledge"
        shots={[{ at: 0, x: 1000, y: 330, s: 1.4 }, { at: b.cue('folders') - 8, dur: 30, x: 1000, y: 260, s: 1.6 }]}
        focus={[
          { from: b.cue('folders') - 4, to: b.cue('scanned') - 4, rect: [575, 222, 900, 54], label: 'Folders or files' },
          { from: b.cue('scanned') - 4, to: b.cue('once') - 4, rect: [575, 299, 900, 42], label: 'Pictures and scanned PDFs (Windows OCR)' },
          { from: b.cue('once') - 4, to: b.cue('ask') - 10, rect: [575, 174, 900, 40], label: 'Read once, kept up to date' },
        ]} />
      <AbsoluteFill style={{ background: `rgba(8,8,12,${0.78 * chat})`, opacity: chat }}>
        <Img src={staticFile('brand/kobii/mint.png')} style={{ position: 'absolute', left: 200, top: 520, width: 200, height: 200 }} />
        <Bubble user text="What does my contract say about the deposit?" show={pop(f, b.cue('ask') - 6)} x={1720} y={300} size={36} />
        <div style={{ position: 'absolute', left: 430, top: 470, width: 1100, opacity: pop(f, b.cue('answer') - 6), transform: `translateY(${(1 - pop(f, b.cue('answer') - 6)) * 30}px)` }}>
          <div style={{ padding: '24px 34px', borderRadius: 30, background: 'white', fontFamily: SANS, fontSize: 34, fontWeight: 700, color: '#141418', lineHeight: 1.35 }}>
            Two months of rent, returned within 30 days after you leave.
          </div>
          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 12, marginTop: 18, padding: '12px 22px', borderRadius: 20, background: 'rgba(242,193,74,0.15)', border: `1.5px solid ${C.accentText}`, opacity: pop(f, b.cue('source') - 6) }}>
            <Fluent name="document_24_filled" size={28} color={C.accentText} />
            <Mono size={24} color={C.text}>Rental contract.pdf · page 4</Mono>
          </div>
        </div>
      </AbsoluteFill>
      <Sfx at={b.cue('ask') - 6} name="pop" volume={0.25} />
      <Sfx at={b.cue('answer') - 6} name="blip" volume={0.25} />
    </AbsoluteFill>
  );
};

/** The four assistant actions in commands, and building your own. */
export const PlugPart: React.FC<{ commands: BeatTimes; create: BeatTimes }> = ({ commands, create }) => {
  const f = useCurrentFrame();
  const actions = FAMILIES.find(x => x.id === 'assistant')!.actions;
  const cues = ['action', 'ask', 'say', 'free'];
  const toCreate = glide(f, create.from - 8, 26);
  return (
    <AbsoluteFill>
      <AbsoluteFill style={{ opacity: 1 - toCreate }}>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 330, display: 'flex', justifyContent: 'center', gap: 26 }}>
          {actions.map((a, i) => {
            const k = pop(f, Math.min(commands.cue('four') - 6 + i * 4, commands.cue(cues[i]) - 6));
            const lit = f >= commands.cue(cues[i]) - 4 && (i === 3 || f < commands.cue(cues[i + 1]) - 4);
            return (
              <div key={a.name} style={{ width: 380, padding: 26, borderRadius: 24, background: lit ? 'rgba(242,193,74,0.14)' : 'rgba(32,32,38,0.97)', border: `2px solid ${lit ? C.accentText : C.line2}`, opacity: k, transform: `translateY(${(1 - k) * 60}px) scale(${lit ? 1.05 : 1})` }}>
                <KdIcon icon={a.icon} color={a.color} size={70} />
                <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text, marginTop: 16 }}>{a.name}</div>
                <div style={{ fontFamily: SANS, fontSize: 21, color: C.dim, marginTop: 6 }}>{a.desc}</div>
              </div>
            );
          })}
        </div>
      </AbsoluteFill>
      <AbsoluteFill style={{ opacity: toCreate }}>
        <div style={{ position: 'absolute', left: 180, top: 330, width: 900, padding: '20px 26px', borderRadius: 16, background: '#0c0c10', border: `1px solid ${C.line2}`, opacity: pop(f, create.cue('sdk') - 6) }}>
          <Mono size={26} color={C.text}><span style={{ color: C.accentText }}>$ </span>dotnet new kdassistant -n Buddy</Mono><br />
          <Mono size={22} color={C.teal}>The template "K-Desktop assistant" was created successfully.</Mono>
        </div>
        <div style={{ position: 'absolute', left: 1220, top: 300, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 20, opacity: pop(f, create.cue('nocode') - 6) }}>
          <div style={{ display: 'flex', gap: 10 }}>
            <PipSprite anim="happy" size={170} fps={6} />
            <PipSprite anim="talk" size={170} fps={8} />
          </div>
          <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text }}>No code: images + JSON</div>
        </div>
      </AbsoluteFill>
      {cues.map(c => <Sfx key={c} at={commands.cue(c) - 6} name="pop-high" volume={0.18} />)}
      <Sfx at={create.cue('sdk') - 6} name="key" volume={0.3} />
      <Sfx at={create.cue('nocode') - 6} name="pop" volume={0.25} />
    </AbsoluteFill>
  );
};
