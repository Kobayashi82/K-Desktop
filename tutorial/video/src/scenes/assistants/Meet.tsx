import React from 'react';
import { AbsoluteFill, Img, Sequence, staticFile, useCurrentFrame } from 'remotion';
import { CaptureClip, CaptureShot, useCapture } from '../../components/Capture';
import { Camera } from '../../components/Camera';
import { Fluent, KdIcon } from '../../components/Icon';
import { Mono } from '../../components/Keys';
import { Sfx } from '../../components/Sfx';
import { Callout } from '../../components/Ui';
import { ease, glide, inOut, OUT, pop } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, SANS } from '../../theme';
import { t } from '../../lib/i18n';

/** Pip, frame by frame (samples/CharacterAssistant): the same pictures K-Desktop animates. */
export const PipSprite: React.FC<{ anim?: string; frames?: number; fps?: number; size?: number; style?: React.CSSProperties }> = ({ anim = 'idle', frames = 4, fps = 5, size = 160, style }) => {
  const f = useCurrentFrame();
  const i = Math.floor((f / 30) * fps) % frames;
  return <Img src={staticFile(`brand/pip/${anim}${i}.png`)} style={{ width: size, height: size, ...style }} />;
};

/** The clip of the Lander, then who is who on a real desktop. */
export const MeetPart: React.FC<{ intro: BeatTimes; cast: BeatTimes }> = ({ intro, cast }) => {
  const f = useCurrentFrame();
  const clipIn = glide(f, 0, 30);
  const toCast = glide(f, cast.from - 10, 26);
  const skins = ['classic', 'apollo', 'capsule', 'saucer', 'pixel'];
  const kobiis = ['mint', 'sky', 'lilac', 'peach', 'lemon'];
  const at = { lander: cast.cue('lander'), game: cast.cue('game'), kobii: cast.cue('kobii'), pip: cast.cue('pip'), json: cast.cue('json') };
  return (
    <AbsoluteFill>
      {/* The real thing: the Lander flies to the pointer and says hello */}
      <AbsoluteFill style={{ opacity: 1 - toCast, perspective: 2000 }}>
        <div style={{
          position: 'absolute', left: 160, top: 120, width: 1600, height: 900, borderRadius: 18, overflow: 'hidden',
          boxShadow: '0 50px 120px rgba(0,0,0,0.7), 0 0 0 1px rgba(255,255,255,0.1)', transform: `rotateX(${8 * (1 - clipIn)}deg) scale(${0.92 + 0.08 * clipIn + 0.04 * ease(f, 30, cast.from)})`,
        }}>
          <Sequence from={0} durationInFrames={cast.from + 20} layout="none">
            <CaptureClip name="clips/lander-says-hello" startFrom={0} />
          </Sequence>
        </div>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 50, textAlign: 'center', fontFamily: SANS, fontSize: 30, fontWeight: 700, color: C.dim, opacity: ease(f, 20, 40) }}>{t('A real recording')}</div>
      </AbsoluteFill>

      {/* Who is who */}
      <AbsoluteFill style={{ opacity: toCast }}>
        <CaptureShot name="desktop-assistants"
          shots={[
            { at: cast.from - 10, x: 960, y: 540, s: 1.0 },
            // Each one framed to a side, clear of the subtitles in the middle.
            { at: at.lander - 8, dur: 30, x: 1518, y: 900, s: 2.4 },
            { at: at.kobii - 8, dur: 30, x: 1520, y: 900, s: 2.4 },
            { at: at.pip - 8, dur: 30, x: 800, y: 900, s: 2.4 },
          ]}>
          <Callout show={inOut(f, at.lander, at.kobii - 4, 8)} x={1255} y={1010} size={22} align="right">{t('Lander')}</Callout>
          <Callout show={inOut(f, at.kobii, at.pip - 4, 8)} x={1800} y={950} size={22} align="center">{t('Kobii')}</Callout>
          <Callout show={inOut(f, at.pip, cast.to + 20, 8)} x={567} y={955} size={22} align="center">{t('Pip')}</Callout>
        </CaptureShot>
        {/* The Lander's game: its skins */}
        <div style={{ position: 'absolute', left: 0, right: 0, top: 150, display: 'flex', justifyContent: 'center', gap: 30, opacity: inOut(f, at.game - 6, at.kobii - 4, 8) }}>
          {skins.map((s, i) => (
            <div key={s} style={{ padding: 20, borderRadius: 24, background: 'rgba(20,20,26,0.92)', border: `1.5px solid ${C.line2}`, transform: `translateY(${(1 - pop(f, at.game - 6 + i * 4)) * 60}px)` }}>
              <Img src={staticFile(`brand/lander/${s}.png`)} style={{ width: 150, height: 150 }} />
            </div>
          ))}
        </div>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 380, textAlign: 'center', fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text, opacity: inOut(f, at.game, at.kobii - 4, 8) }}>{t('Missions · races · levels · skins')}</div>
        {/* Kobii's colours */}
        <div style={{ position: 'absolute', left: '50%', transform: 'translateX(-50%)', top: 150, display: 'flex', justifyContent: 'center', gap: 26, padding: '16px 30px', borderRadius: 30, background: 'rgba(20,20,26,0.92)', border: `1.5px solid ${C.line2}`, opacity: inOut(f, at.kobii + 4, at.pip - 4, 8) }}>
          {kobiis.map((s, i) => <Img key={s} src={staticFile(`brand/kobii/${s}.png`)} style={{ width: 170, height: 170, transform: `translateY(${(1 - pop(f, at.kobii + 4 + i * 4)) * 60 + Math.sin(f / 8 + i) * 6}px)` }} />)}
        </div>
        {/* Pip: pictures and JSON */}
        <div style={{ position: 'absolute', left: 160, top: 130, display: 'flex', gap: 40, alignItems: 'center', opacity: inOut(f, at.pip + 4, cast.to + 30, 8) }}>
          <div style={{ display: 'flex', gap: 10, padding: 16, borderRadius: 24, background: 'rgba(20,20,26,0.92)', border: `1.5px solid ${C.line2}` }}>
            {['idle', 'talk', 'happy', 'listen'].map(a => <PipSprite key={a} anim={a} frames={a === 'listen' ? 2 : 4} size={130} />)}
          </div>
          <div style={{ padding: '18px 24px', borderRadius: 18, background: '#0c0c10', border: `1px solid ${C.line2}`, opacity: ease(f, at.json - 8, at.json + 4) }}>
            <Mono size={20} color={C.dim}>{t('character.json')}</Mono><br />
            <Mono size={20} color={C.accentText}>{t('"talk": { "frames": [ "frames/talk0.png", … ] }')}</Mono>
          </div>
        </div>
      </AbsoluteFill>
      <Sfx at={at.lander - 8} name="whoosh-short" volume={0.22} />
      <Sfx at={at.kobii - 8} name="whoosh-short" volume={0.22} />
      <Sfx at={at.pip - 8} name="whoosh-short" volume={0.22} />
      {skins.map((_, i) => <Sfx key={i} at={at.game - 6 + i * 4} name="pop-high" volume={0.1} />)}
    </AbsoluteFill>
  );
};

/** The optional module, and installing a .kai. */
export const InstallPart: React.FC<{ module: BeatTimes; install: BeatTimes }> = ({ module, install }) => {
  const f = useCurrentFrame();
  const card = useCapture('assistant-install');
  const toInstall = glide(f, install.from - 8, 26);
  const S = 1.3;
  const focus = [
    { at: install.cue('card'), until: install.cue('signature'), rect: [20, 58, 440, 130] },
    { at: install.cue('signature'), until: install.cue('what'), rect: [22, 348, 436, 62] },
    { at: install.cue('what'), until: install.cue('installs'), rect: [20, 200, 440, 112] },
    { at: install.cue('installs'), until: install.to + 20, rect: [345, 578, 113, 34] },
  ];
  const drop = ease(f, install.cue('installs') + 14, install.cue('installs') + 40, OUT);
  return (
    <AbsoluteFill>
      <AbsoluteFill style={{ opacity: 1 - toInstall, transform: `scale(${1 + 0.1 * toInstall})` }}>
        <CaptureShot name="assistant-settings"
          shots={[{ at: 0, x: 960, y: 400, s: 1.2 }, { at: module.cue('optional') - 10, dur: 30, x: 960, y: 230, s: 1.7 }]}
          focus={[
            { from: module.cue('optional') - 4, to: module.cue('off') - 2, rect: [571, 164, 776, 38], label: t('Turn on the Assistant module') },
            { from: module.cue('off') - 2, to: module.to + 30, rect: [571, 126, 776, 22], label: t('Off: nothing of it is loaded') },
          ]} />
      </AbsoluteFill>
      <AbsoluteFill style={{ opacity: toInstall }}>
        {/* A .kai file */}
        <div style={{ position: 'absolute', left: 260, top: 380, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 16, transform: `scale(${pop(f, install.cue('package') - 8)})`, opacity: 1 - ease(f, install.cue('card') - 4, install.cue('card') + 8) * 0.6 }}>
          <div style={{ position: 'relative' }}>
            <Fluent name="document_24_filled" size={220} color="#3a3a44" />
            <Img src={staticFile('brand/lander/icon.png')} style={{ position: 'absolute', left: 50, top: 60, width: 120, height: 120 }} />
          </div>
          <Mono size={30} color={C.text}>{t('lander-0.16.1.kai')}</Mono>
        </div>
        <div style={{ position: 'absolute', left: 960 - (card.width * S) / 2 + 120, top: 48, transform: `scale(${0.9 + 0.1 * pop(f, install.cue('card') - 6)}) translateY(${-drop * 900}px)`, opacity: Math.min(1, pop(f, install.cue('card') - 6) * 2) * (1 - drop) }}>
          <div style={{ position: 'relative', width: card.width * S, height: card.height * S, borderRadius: 14, overflow: 'hidden', boxShadow: '0 40px 100px rgba(0,0,0,0.7), 0 0 0 1px rgba(255,255,255,0.12)' }}>
            <Img src={staticFile(card.file)} style={{ width: card.width * S, height: card.height * S }} />
            {focus.map((x, i) => {
              const k = inOut(f, x.at - 4, x.until, 6);
              if (k <= 0) return null;
              const [rx, ry, rw, rh] = x.rect;
              return <div key={i} style={{ position: 'absolute', left: rx * S - 6, top: ry * S - 6, width: rw * S + 12, height: rh * S + 12, borderRadius: 12, border: `3px solid ${C.accentText}`, boxShadow: `0 0 0 9999px rgba(5,5,8,${0.55 * k}), 0 0 20px rgba(242,193,74,0.7)`, opacity: k }} />;
            })}
          </div>
        </div>
        {/* It lands on the desktop */}
        <div style={{ position: 'absolute', left: 960, top: 540 + (1 - drop) * -600, transform: 'translate(-50%, -50%)', opacity: drop }}>
          <Img src={staticFile('brand/lander/classic.png')} style={{ width: 260, height: 260 }} />
        </div>
      </AbsoluteFill>
      <Sfx at={module.cue('optional') - 4} name="click" volume={0.25} />
      <Sfx at={install.cue('package') - 8} name="pop" volume={0.25} />
      <Sfx at={install.cue('card') - 6} name="whoosh-short" volume={0.2} />
      <Sfx at={install.cue('installs')} name="click" volume={0.3} />
      <Sfx at={install.cue('installs') + 36} name="impact" volume={0.25} />
    </AbsoluteFill>
  );
};

export { Camera, KdIcon };
