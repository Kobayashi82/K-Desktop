import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Camera, Shot } from '../../components/Camera';
import { Wire } from '../../components/Diagram';
import { Fluent, KdIcon } from '../../components/Icon';
import { Mono } from '../../components/Keys';
import { Sfx } from '../../components/Sfx';
import { speechAmp, Wave } from '../../components/Wave';
import { ease, OUT, pop, rand } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, SANS } from '../../theme';
import { t } from '../../lib/i18n';

const CW = 640;
const CH = 600;
const cardX = (i: number) => 200 + i * 800;
const CY = 240;
const STAGE_W = cardX(6) + 200;

const Stage: React.FC<{ i: number; at: number; icon: string; color: string; title: string; tech: string; lit: number; children: React.ReactNode }> = ({ i, at, icon, color, title, tech, lit, children }) => {
  const f = useCurrentFrame();
  const k = pop(f, at - 10, { damping: 15, stiffness: 120 });
  return (
    <div style={{
      position: 'absolute', left: cardX(i), top: CY, width: CW, height: CH, borderRadius: 30, boxSizing: 'border-box', padding: 30,
      background: 'rgba(30,30,36,0.97)', border: `2px solid ${lit > 0.05 ? `rgba(242,193,74,${0.3 + 0.6 * lit})` : C.line2}`,
      boxShadow: `0 30px 70px rgba(0,0,0,0.5)${lit > 0.05 ? `, 0 0 70px rgba(242,193,74,${0.25 * lit})` : ''}`,
      opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 100}px)`,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 18 }}>
        <div style={{ fontFamily: SANS, fontSize: 22, fontWeight: 800, color: C.faint, width: 30 }}>{i + 1}</div>
        <KdIcon icon={icon} color={color} size={64} />
        <div>
          <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text }}>{title}</div>
          <div style={{ fontFamily: SANS, fontSize: 19, color: C.accentText, fontWeight: 600 }}>{tech}</div>
        </div>
      </div>
      <div style={{ position: 'absolute', left: 30, right: 30, top: 140, bottom: 30 }}>{children}</div>
    </div>
  );
};

const Label: React.FC<{ children: React.ReactNode; color?: string }> = ({ children, color = C.dim }) => (
  <div style={{ fontFamily: SANS, fontSize: 19, fontWeight: 700, color, marginBottom: 6, letterSpacing: 0.5 }}>{children}</div>
);

const Meter: React.FC<{ label: string; value: number; need: number }> = ({ label, value, need }) => (
  <div style={{ marginBottom: 26 }}>
    <div style={{ display: 'flex', justifyContent: 'space-between', fontFamily: SANS, fontSize: 21, color: C.text, marginBottom: 10 }}>
      <span>{label}</span><Mono size={21} color={value >= need ? C.success : C.dim}>{value.toFixed(2)}</Mono>
    </div>
    <div style={{ position: 'relative', height: 16, borderRadius: 8, background: 'rgba(255,255,255,0.08)' }}>
      <div style={{ width: `${value * 100}%`, height: '100%', borderRadius: 8, background: value >= need ? C.success : C.teal }} />
      <div style={{ position: 'absolute', left: `${need * 100}%`, top: -6, bottom: -6, width: 3, background: C.accentText }} />
    </div>
  </div>
);

/** What happens to every sound, stage by stage; the camera follows it along. */
export const PipelinePart: React.FC<{ echo: BeatTimes; detector: BeatTimes; verify: BeatTimes; vad: BeatTimes; stt: BeatTimes }> = ({ echo, detector, verify, vad, stt }) => {
  const f = useCurrentFrame();
  const starts = [echo.from, detector.from, detector.cue('personal'), verify.from, vad.from, stt.from];
  const current = starts.reduce((c, t, i) => (f >= t - 10 ? i : c), 0);
  const lit = (i: number) => (i === current ? ease(f, starts[i] - 10, starts[i] + 10) : 0);
  const shots: Shot[] = [
    { at: 0, x: cardX(0) + CW / 2, y: 560, s: 1.25, ry: -8 },
    ...starts.map((t, i) => ({ at: t - 16, dur: 30, x: cardX(i) + CW / 2, y: 545, s: 1.4, ry: 0 })),
    { at: stt.cue('text') + 26, dur: 50, x: STAGE_W / 2, y: 560, s: 0.4, rx: 8 },
  ];
  const echoK = ease(f, echo.cue('echo'), echo.cue('echo') + 40, OUT);
  const col = Math.max(0, Math.floor((f - detector.cue('eighty')) / 2.4));
  const fp = ease(f, detector.cue('fingerprint') - 4, detector.cue('fingerprint') + 10);
  const dtw = 0.2 + 0.68 * ease(f, detector.cue('two') - 30, detector.cue('agree'));
  const cls = 0.15 + 0.78 * ease(f, detector.cue('two') - 10, detector.cue('agree') + 4);
  const fired = f > detector.cue('agree') + 4;
  const transcript = 'Kay desktop, open the…'.slice(0, Math.max(0, Math.floor((f - verify.cue('parakeet')) / 1.6)));
  const confirmed = f > verify.cue('confirms');
  const vadEnd = ease(f, vad.cue('stop') - 10, vad.cue('stop'));
  const text = 'open the browser'.slice(0, Math.max(0, Math.floor((f - stt.cue('text') + 20) / 1.5)));
  return (
    <AbsoluteFill>
      <Camera shots={shots} width={STAGE_W}>
        {[0, 1, 2, 3, 4].map(i => (
          <Wire key={i} from={[cardX(i) + CW, CY + CH / 2]} to={[cardX(i + 1), CY + CH / 2]} draw={ease(f, starts[i + 1] - 20, starts[i + 1])} flow={1} color={C.teal} width={5} seed={i} />
        ))}
        <Stage i={0} at={starts[0]} icon="headphones_sound_wave_24_filled" color="purple" title={t('Echo cancellation')} tech={t('Speex MDF filter')} lit={lit(0)}>
          <Label>{t('MICROPHONE: your voice + the music')}</Label>
          <div style={{ position: 'relative', height: 90 }}>
            <div style={{ position: 'absolute', inset: 0, opacity: 1 - 0.85 * echoK }}><Wave width={580} height={90} color="rgba(255,140,198,0.8)" amp={() => 0.5} seed={3} /></div>
            <div style={{ position: 'absolute', inset: 0 }}><Wave width={580} height={90} color={C.teal} amp={speechAmp(0.15, 0.85, 4)} seed={7} /></div>
          </div>
          <Label>{t('WHAT THE PC IS PLAYING')}</Label>
          <Wave width={580} height={70} color="rgba(255,140,198,0.8)" amp={() => 0.5} seed={3} />
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 18, opacity: echoK }}>
            <Fluent name="arrow_down_24_filled" size={30} color={C.accentText} />
            <span style={{ fontFamily: SANS, fontSize: 26, fontWeight: 800, color: C.accentText }}>{t('Just your voice')}</span>
          </div>
        </Stage>
        <Stage i={1} at={starts[1]} icon="data_histogram_24_filled" color="sky" title="openWakeWord" tech={t('Mel spectrogram · speech embedding')} lit={lit(1)}>
          <Label>{t('ONE FINGERPRINT EVERY 80 MS')}</Label>
          <div style={{ position: 'relative', display: 'flex', gap: 3, height: 300, alignItems: 'flex-end', overflow: 'hidden' }}>
            {Array.from({ length: 34 }, (_, c) => {
              const n = col - 33 + c;
              return (
                <div key={c} style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                  {Array.from({ length: 12 }, (_, r) => {
                    const v = n < 0 ? 0.05 : rand(n * 13 + r) * speechAmp(0, 1, 7)((n % 60) / 60);
                    return <div key={r} style={{ width: 14, height: 21, borderRadius: 3, background: `rgba(85,210,255,${0.1 + 0.85 * v})` }} />;
                  })}
                </div>
              );
            })}
            <div style={{ position: 'absolute', right: 0, top: -4, bottom: -4, width: 16 * 17 + 2, border: `3px solid ${C.accentText}`, borderRadius: 10, opacity: fp, boxShadow: `0 0 20px rgba(242,193,74,0.5)` }} />
          </div>
          <div style={{ marginTop: 14, fontFamily: SANS, fontSize: 22, color: C.accentText, fontWeight: 700, opacity: fp }}>{t('The last 1.28 s, as 16 × 96 numbers')}</div>
        </Stage>
        <Stage i={2} at={starts[2]} icon="person_voice_24_filled" color="teal" title={t('Your detector')} tech={t('Trained on your recordings')} lit={lit(2)}>
          <Meter label={t('Like your takes (DTW)')} value={dtw} need={0.62} />
          <Meter label={t('Classifier (your voice vs. normal talk)')} value={cls} need={0.62} />
          <div style={{ display: 'flex', alignItems: 'center', gap: 18, marginTop: 14 }}>
            <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.dim }}>{t('both agree')}</div>
            <Fluent name="arrow_right_24_filled" size={34} color={C.dim} />
            <div style={{ padding: '12px 22px', borderRadius: 16, fontFamily: SANS, fontSize: 30, fontWeight: 800, background: fired ? C.accentText : 'rgba(255,255,255,0.06)', color: fired ? '#1a1306' : C.faint, boxShadow: fired ? '0 0 40px rgba(242,193,74,0.6)' : 'none' }}>{t('Wake word!')}</div>
          </div>
        </Stage>
        <Stage i={3} at={starts[3]} icon="checkmark_starburst_24_filled" color="green" title={t('Second check')} tech={t('Parakeet transcribes, compares by sound')} lit={lit(3)}>
          <Label>{t('LAST 2 SECONDS, TRANSCRIBED')}</Label>
          <div style={{ height: 70, borderRadius: 14, background: '#15151a', display: 'flex', alignItems: 'center', padding: '0 20px' }}><Mono size={28}>{transcript}</Mono></div>
          <Label>{t('SOUNDS LIKE YOUR WORD?')}</Label>
          <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginTop: 6, opacity: ease(f, verify.cue('confirms') - 30, verify.cue('confirms') - 16) }}>
            <Mono size={32} color={C.text}>{t('kadesktop')}</Mono>
            <span style={{ fontFamily: SANS, fontSize: 34, color: C.faint }}>≈</span>
            <Mono size={32} color={C.accentText}>{t('kdesktop')}</Mono>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginTop: 24, opacity: confirmed ? 1 : 0, transform: `scale(${confirmed ? pop(f, verify.cue('confirms')) : 0})`, transformOrigin: 'left center' }}>
            <Fluent name="checkmark_circle_24_filled" size={44} color={C.success} />
            <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.success }}>{t('0.93 — it\'s really you')}</span>
          </div>
        </Stage>
        <Stage i={4} at={starts[4]} icon="pulse_24_filled" color="orange" title={t('Silero VAD')} tech={t('Voice activity detection')} lit={lit(4)}>
          <Label>{t('WHEN YOU SPEAK, WHEN YOU STOP')}</Label>
          <div style={{ position: 'relative', height: 200 }}>
            <Wave width={580} height={200} color={C.teal} amp={speechAmp(0.05, 0.62, 6)} scroll={false} seed={11} />
            <div style={{ position: 'absolute', left: '5%', width: '57%', top: 0, bottom: 0, background: 'rgba(62,220,193,0.12)', border: `2px solid rgba(62,220,193,0.5)`, borderRadius: 10 }} />
            <div style={{ position: 'absolute', left: '62%', width: `${24 * vadEnd}%`, top: 0, bottom: 0, background: 'repeating-linear-gradient(45deg, rgba(255,255,255,0.06) 0 10px, transparent 10px 20px)' }} />
            <div style={{ position: 'absolute', left: '86%', top: -10, bottom: -10, width: 4, background: C.accentText, opacity: vadEnd }} />
          </div>
          <div style={{ marginTop: 20, fontFamily: SANS, fontSize: 26, fontWeight: 800, color: C.accentText, opacity: vadEnd }}>{t('Silence: the order is over')}</div>
        </Stage>
        <Stage i={5} at={starts[5]} icon="text_t_24_filled" color="indigo" title={t('Speech to text')} tech={t('NVIDIA Parakeet TDT 0.6B · multilingual')} lit={lit(5)}>
          <Wave width={580} height={120} color={C.teal} amp={speechAmp(0.05, 0.95, 6)} scroll={false} seed={13} />
          <div style={{ display: 'flex', justifyContent: 'center', margin: '14px 0' }}><Fluent name="arrow_down_24_filled" size={40} color={C.accentText} /></div>
          <div style={{ height: 110, borderRadius: 18, background: '#15151a', border: `2px solid ${text ? C.accentText : C.line}`, display: 'flex', alignItems: 'center', padding: '0 26px' }}>
            <span style={{ fontFamily: SANS, fontSize: 44, fontWeight: 800, color: C.text }}>{text ? `“${text}”` : ''}</span>
          </div>
          <div style={{ marginTop: 18, display: 'flex', gap: 10 }}>
            {['int8', 'sherpa-onnx', 'offline'].map(t => <span key={t} style={{ fontFamily: SANS, fontSize: 18, padding: '5px 12px', borderRadius: 10, background: 'rgba(255,255,255,0.07)', color: C.dim }}>{t}</span>)}
          </div>
        </Stage>
      </Camera>
      {starts.map((t, i) => <Sfx key={i} at={t - 16} name="whoosh-short" volume={0.2} />)}
      <Sfx at={detector.cue('agree') + 4} name="chime" volume={0.22} />
      <Sfx at={verify.cue('confirms')} name="blip" volume={0.25} />
      <Sfx at={stt.cue('text') + 26} name="whoosh" volume={0.25} />
    </AbsoluteFill>
  );
};
