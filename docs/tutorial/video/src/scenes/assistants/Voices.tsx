import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { CaptureShot } from '../../components/Capture';
import { Fluent } from '../../components/Icon';
import { Sfx } from '../../components/Sfx';
import { VoiceBars } from '../../components/VoiceBars';
import { speechAmp, Wave } from '../../components/Wave';
import { ease, inOut, pop } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, SANS } from '../../theme';

const PROFILES = ['Natural', 'Mission control', 'Classic robot', 'Martian', '8-bit', 'Beeps'];

/** Answering out loud, sentence by sentence; Kokoro, Piper, Windows voices; styles, mixing, panning. */
export const VoicesPart: React.FC<{ voice: BeatTimes; voice2: BeatTimes }> = ({ voice, voice2 }) => {
  const f = useCurrentFrame();
  const sentences = ['Copy that.', 'Dentist at 17:30 tomorrow.', 'I’ll remind you at 16:45.'];
  const sAt = voice.cue('sentence');
  const capShow = inOut(f, voice.cue('kokoro') - 10, voice2.from, 10);
  const hearing = inOut(f, voice.cue('hearing') - 6, voice.cue('piper') - 4, 8);
  const styleAt = voice2.cue('style');
  const sideK = ease(f, voice2.cue('side') - 6, voice2.cue('side') + 14);
  return (
    <AbsoluteFill>
      {/* Sentence by sentence */}
      <AbsoluteFill style={{ opacity: 1 - capShow * (f < voice2.from ? 1 : 0) }}>
        {f < voice.cue('kokoro') + 10 && (
          <>
            <Img src={staticFile('brand/lander/classic.png')} style={{ position: 'absolute', left: 300, top: 360 + Math.sin(f / 14) * 8, width: 280, height: 280 }} />
            <div style={{ position: 'absolute', left: 660, top: 300, display: 'flex', flexDirection: 'column', gap: 26 }}>
              {sentences.map((s, i) => {
                const at = sAt + i * 26;
                const k = pop(f, at);
                const playing = f >= at && f < at + 26;
                return (
                  <div key={s} style={{ display: 'flex', alignItems: 'center', gap: 22, opacity: k, transform: `translateX(${(1 - k) * 40}px)` }}>
                    <div style={{ padding: '16px 28px', borderRadius: 30, background: 'white', fontFamily: SANS, fontSize: 36, fontWeight: 700, color: '#141418' }}>{s}</div>
                    <div style={{ opacity: playing ? 1 : 0.25 }}><Wave width={180} height={56} color={C.teal} amp={speechAmp(0, 1, 3)} seed={i * 5} /></div>
                  </div>
                );
              })}
              <div style={{ display: 'flex', gap: 10, marginLeft: 10, opacity: f > sAt + 70 ? 0 : ease(f, sAt, sAt + 4) }}>
                {[0, 1, 2].map(i => <div key={i} style={{ width: 14, height: 14, borderRadius: 7, background: C.dim, opacity: 0.3 + 0.7 * Math.abs(Math.sin(f / 6 + i)) }} />)}
              </div>
            </div>
          </>
        )}
      </AbsoluteFill>
      {/* The voices in Settings */}
      <AbsoluteFill style={{ opacity: capShow }}>
        <CaptureShot name="assistant-voice"
          shots={[{ at: voice.cue('kokoro') - 10, x: 1000, y: 400, s: 1.45 }, { at: voice.cue('piper') - 8, dur: 30, x: 1000, y: 560, s: 1.5 }, { at: voice.cue('windows') - 8, dur: 30, x: 1000, y: 700, s: 1.5 }]}
          focus={[
            { from: voice.cue('kokoro') - 4, to: voice.cue('hearing') - 6, rect: [572, 323, 910, 100], label: 'Kokoro · natural voice · offline' },
            { from: voice.cue('piper') - 4, to: voice.cue('windows') - 4, rect: [572, 538, 910, 100], label: 'Piper · voices in every language' },
            { from: voice.cue('windows') - 4, to: voice2.from, rect: [572, 740, 910, 42], label: 'Windows voices · or beeps' },
          ]} />
      </AbsoluteFill>
      {/* «The same voice you're hearing right now» */}
      <AbsoluteFill style={{ background: `rgba(8,8,12,${0.82 * hearing})`, opacity: hearing }}>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 360, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 30 }}>
          <VoiceBars beat="voice" bars={40} width={900} height={200} color={C.teal} gain={1.4} />
          <div style={{ fontFamily: SANS, fontSize: 40, fontWeight: 800, color: C.text }}>This narration is Kokoro</div>
        </div>
      </AbsoluteFill>
      {/* Styles */}
      {f > voice2.from - 10 && (
        <AbsoluteFill style={{ opacity: ease(f, voice2.from - 10, voice2.from + 6) }}>
          <div style={{ position: 'absolute', left: 0, right: 0, top: 200, display: 'flex', justifyContent: 'center', gap: 16, flexWrap: 'wrap', width: 1500, margin: '0 auto' }}>
            {PROFILES.map((p, i) => {
              const lit = (i === 1 && f > voice2.cue('radio') - 4 && f < voice2.cue('mix')) || ((i === 2 || i === 4) && f > voice2.cue('robot') - 4 && f < voice2.cue('quiet'));
              return (
                <span key={p} style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, padding: '14px 26px', borderRadius: 30, background: lit ? C.accentText : 'rgba(30,30,36,0.96)', color: lit ? '#1a1306' : C.text, border: `1.5px solid ${C.line2}`, opacity: pop(f, styleAt - 6 + i * 3) }}>{p}</span>
              );
            })}
          </div>
          {/* Mix two voices */}
          <div style={{ position: 'absolute', left: 960, top: 560, transform: 'translate(-50%, -50%)', opacity: inOut(f, voice2.cue('mix') - 6, voice2.cue('quiet') - 4, 8), display: 'flex', alignItems: 'center' }}>
            {['Heart', 'Bella'].map((v, i) => (
              <div key={v} style={{ width: 240, height: 240, borderRadius: 120, marginLeft: i ? -70 : 0, background: i ? 'rgba(188,140,255,0.35)' : 'rgba(62,220,193,0.35)', border: `3px solid ${i ? C.violet : C.teal}`, display: 'flex', alignItems: 'center', justifyContent: 'center', fontFamily: SANS, fontSize: 32, fontWeight: 800, color: C.text, transform: `translateX(${(i ? -1 : 1) * 30 * ease(f, voice2.cue('mix'), voice2.cue('mix') + 20)}px)` }}>{v}</div>
            ))}
          </div>
          {/* Quiet with a film on */}
          <div style={{ position: 'absolute', left: 960, top: 600, transform: 'translate(-50%, -50%)', opacity: inOut(f, voice2.cue('quiet') - 6, voice2.cue('side') - 6, 8), display: 'flex', alignItems: 'center', gap: 30 }}>
            <div style={{ width: 420, height: 240, borderRadius: 14, background: 'linear-gradient(135deg,#0c2a3a,#3a1030)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <Fluent name="play_24_filled" size={80} color="rgba(255,255,255,0.7)" />
            </div>
            <Fluent name="speaker_mute_24_filled" size={80} color={C.accentText} />
            <span style={{ fontFamily: SANS, fontSize: 32, fontWeight: 800, color: C.text }}>Bubble only</span>
          </div>
          {/* They sound from where they are */}
          <div style={{ position: 'absolute', left: 960, top: 620, transform: 'translate(-50%, -50%)', opacity: sideK, display: 'flex', alignItems: 'center', gap: 60 }}>
            <Fluent name="speaker_2_24_filled" size={80 + 0 * sideK} color={C.faint} />
            <div style={{ position: 'relative', width: 640, height: 300, borderRadius: 16, border: '6px solid #2a2a30', background: 'linear-gradient(135deg,#1c2a5a,#3a1f6a)' }}>
              <Img src={staticFile('brand/lander/classic.png')} style={{ position: 'absolute', left: 140 + 360 * sideK, top: 90, width: 120, height: 120 }} />
            </div>
            <div style={{ position: 'relative' }}>
              <Fluent name="speaker_2_24_filled" size={110} color={C.accentText} />
              {[0, 1, 2].map(i => <div key={i} style={{ position: 'absolute', left: 60, top: 55 - 20 * (i + 1), width: 40 * (i + 1), height: 40 * (i + 1), borderRadius: '50%', border: `3px solid rgba(242,193,74,${0.6 - i * 0.15})`, transform: `scale(${1 + 0.1 * Math.sin(f / 5 + i)})` }} />)}
            </div>
          </div>
        </AbsoluteFill>
      )}
      {sentences.map((_, i) => <Sfx key={i} at={sAt + i * 26} name="blip" volume={0.15} />)}
      <Sfx at={voice2.cue('radio') - 4} name="blip-low" volume={0.25} />
      <Sfx at={voice2.cue('mix')} name="whoosh-short" volume={0.2} />
    </AbsoluteFill>
  );
};
