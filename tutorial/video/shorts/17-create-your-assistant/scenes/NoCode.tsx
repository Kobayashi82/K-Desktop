import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Part } from '../../../src/components/Chapter';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { Mono } from '../../../src/components/Keys';
import { Sfx } from '../../../src/components/Sfx';
import { Showcase } from '../../../src/components/Showcase';
import { ease, glide, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { PipSprite } from '../../../src/scenes/assistants/Meet';
import { Code, Terminal } from './Parts';

/** Pip walking: its walk is one strip of six frames (frames/walk.png), as character.json says. */
const PipWalk: React.FC<{ size: number }> = ({ size }) => {
  const f = useCurrentFrame();
  const i = Math.floor((f / 30) * 10) % 6;
  return <div style={{ width: size, height: size, backgroundImage: `url(${staticFile('brand/pip/walk.png')})`, backgroundSize: `${size * 6}px ${size}px`, backgroundPosition: `${-i * size}px 0` }} />;
};

// The folder of a character (samples/CharacterAssistant/Package).
const FILES: Array<[string, string, string, string]> = [
  ['manifest', 'document_24_regular', 'assistant.json', t('Its manifest: "entry": "character.json"')],
  ['character', 'document_24_regular', 'character.json', t('Its height and animations')],
  ['icon', 'image_24_regular', 'icon.png', t('Its icon')],
  ['frames', 'folder_24_filled', 'frames\\', t('PNG with transparency, all the same height')],
];

/** The files of a character, and its character.json. */
const Files: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const x = B('files');
  const card = glide(f, 0, 22);
  const json = glide(f, x.cue('character') - 4, 20);
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 140, top: 200, width: 820, padding: '24px 28px', borderRadius: 20, boxSizing: 'border-box', background: 'rgba(30,30,36,0.96)', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 70px rgba(0,0,0,0.5)', opacity: card }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 14 }}>
          <Fluent name="folder_open_24_filled" size={36} color={C.accentText} />
          <Mono size={28}>MyCharacter\</Mono>
        </div>
        {FILES.map(([cue, icon, name, what]) => {
          const lit = ease(f, x.cue(cue) - 4, x.cue(cue) + 6, OUT);
          return (
            <div key={cue} style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '14px 16px', borderRadius: 12, marginLeft: 30, background: `rgba(242,193,74,${0.14 * lit})`, borderLeft: `4px solid rgba(242,193,74,${lit})` }}>
              <Fluent name={icon} size={30} color={cue === 'frames' ? C.accentText : C.dim} />
              <div>
                <Mono size={26}>{name}</Mono>
                <div style={{ fontSize: 20, color: C.dim, marginTop: 2, opacity: 0.4 + 0.6 * lit }}>{what}</div>
              </div>
            </div>
          );
        })}
        <div style={{ display: 'flex', gap: 8, marginTop: 14, marginLeft: 76, opacity: ease(f, x.cue('frames') - 4, x.cue('frames') + 8) }}>
          {['idle0', 'idle1', 'talk0', 'happy0', 'listen0', 'think0'].map(n => (
            <div key={n} style={{ borderRadius: 8, background: 'repeating-conic-gradient(#3a3a42 0% 25%, #2a2a30 0% 50%) 50% / 16px 16px' }}>
              <Img src={staticFile(`brand/pip/${n}.png`)} style={{ width: 96, height: 96, display: 'block' }} />
            </div>
          ))}
        </div>
      </div>
      <div style={{ opacity: json, transform: `translateX(${(1 - json) * 60}px)` }}>
        <Code file="character.json" width={820} size={22} style={{ left: 1000, top: 250 }} lines={[
          '{',
          '  "height": 80,',
          '  "faces": "right",',
          '  "animations": {',
          '    "idle": { "frames": ["frames/idle0.png", …], "fps": 2 },',
          '    "walk": { "strip": "frames/walk.png", "frames": 6 },',
          '    "talk": { "frames": ["frames/talk0.png", …], "fps": 8 }',
          '  }',
          '}',
        ]} />
      </div>
      <Sfx at={x.cue('character') - 4} name="whoosh-short" volume={0.18} />
      {FILES.map(([cue]) => <Sfx key={cue} at={x.cue(cue) - 4} name="blip" volume={0.12} />)}
    </AbsoluteFill>
  );
};

/** The animations by name: idle (the only required one), walk, talk, listen, think; a missing one uses another. */
const Anims: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const a = B('anims');
  const CARDS: Array<[string, React.ReactNode]> = [
    ['idle', <PipSprite anim="idle" size={170} fps={4} />],
    ['walk', <PipWalk size={170} />],
    ['talk', <PipSprite anim="talk" size={170} fps={8} />],
    ['listen', <PipSprite anim="listen" frames={2} size={170} fps={3} />],
    ['think', <PipSprite anim="think" frames={2} size={170} fps={2} />],
  ];
  const fall = ease(f, a.cue('fallback') - 4, a.cue('fallback') + 10, OUT);
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 190, display: 'flex', justifyContent: 'center', gap: 26 }}>
        {CARDS.map(([name, sprite]) => {
          const k = pop(f, a.cue(name) - 6, { damping: 14 });
          const required = name === 'idle';
          return (
            <div key={name} style={{
              width: 260, padding: '20px 0 22px', borderRadius: 24, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10,
              background: 'rgba(30,30,36,0.94)', border: `2px solid ${required ? C.accentText : C.line2}`, opacity: Math.min(1, k * 1.4), transform: `translateY(${(1 - k) * 50}px)`,
            }}>
              {sprite}
              <Mono size={30} color={required ? C.accentText : C.text}>{name}</Mono>
              <div style={{ fontSize: 20, color: required ? C.accentText : 'transparent', fontWeight: 700 }}>{t('the only required one')}</div>
            </div>
          );
        })}
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 640, display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 22, opacity: fall }}>
        {['think', 'listen', 'idle'].map((n, i) => (
          <React.Fragment key={n}>
            {i > 0 && <Fluent name="arrow_right_24_filled" size={36} color={C.accentText} />}
            <div style={{ padding: '12px 26px', borderRadius: 30, background: 'rgba(40,40,48,0.94)', border: `1.5px solid ${C.line2}` }}><Mono size={28}>{n}</Mono></div>
          </React.Fragment>
        ))}
        <div style={{ marginLeft: 20, fontSize: 28, fontWeight: 700, color: C.dim }}>{t('a missing one uses another')}</div>
      </div>
      {CARDS.map(([name]) => <Sfx key={name} at={a.cue(name) - 6} name="pop-high" volume={0.14} />)}
    </AbsoluteFill>
  );
};

/** What K-Desktop does with it: Pip's real clips. */
const Moves: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const m = B('moves');
  const clips: Array<[string, number, string]> = [
    ['Pip/enter.summoned', m.from - 8, t('It stands on the taskbar or on your windows')],
    ['Pip/walk', m.cue('strolls') - 4, t('It strolls')],
    ['Pip/sleep', m.cue('sleeps') - 4, t('It sleeps when you are away')],
    ['Pip/state.speaking', m.cue('talks') - 4, t('It talks, thinks and uses tools')],
  ];
  const current = [...clips].reverse().find(([, at]) => f >= at) ?? clips[0];
  const k = glide(f, m.from - 8, 22);
  const pip = pop(f, m.cue('pip') - 6, { damping: 14 });
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 960, top: 470, transform: 'translate(-50%, -50%)', opacity: k }}>
        <Showcase key={current[0]} clip={current[0]} width={1180} from={current[1]} label={current[2]} />
      </div>
      <div style={{
        position: 'absolute', right: 120, top: 40, display: 'flex', alignItems: 'center', gap: 14, padding: '12px 24px', borderRadius: 30,
        background: 'linear-gradient(180deg, #FFD970, #F2B807)', color: '#1a1306', fontSize: 26, fontWeight: 800, transform: `scale(${pip})`, opacity: Math.min(1, pip * 1.4),
      }}>
        <Img src={staticFile('brand/pip/icon.png')} style={{ width: 40, height: 40 }} /> {t('Pip: samples/CharacterAssistant')}
      </div>
      {clips.slice(1).map(([c, at]) => <Sfx key={c} at={at} name="whoosh-short" volume={0.15} />)}
    </AbsoluteFill>
  );
};

/** pack.ps1 makes the .kai; K-Desktop opens it. */
const Pack: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const p = B('pack');
  const file = pop(f, p.cue('open') - 8, { damping: 14 });
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <Terminal width={1100} height={260} style={{ left: 160, top: 260 }} lines={[
        { at: p.from - 4, cmd: 'tools\\character\\pack.ps1 MyCharacter' },
        { at: p.from + 46, out: 'mycharacter-1.0.0.kai', color: C.teal },
      ]} />
      <div style={{ position: 'absolute', left: 1420, top: 230, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 16, transform: `scale(${file})`, opacity: Math.min(1, file * 1.4) }}>
        <div style={{ position: 'relative' }}>
          <Fluent name="document_24_filled" size={240} color="#3a3a44" />
          <Img src={staticFile('brand/pip/icon.png')} style={{ position: 'absolute', left: 60, top: 70, width: 120, height: 120 }} />
        </div>
        <Mono size={28}>mycharacter-1.0.0.kai</Mono>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '12px 22px', borderRadius: 12, background: 'rgba(40,40,48,0.96)', border: `1.5px solid ${C.line2}`, fontSize: 24, color: C.text }}>
          <KdIcon icon="open_24_filled" color="gold" size={40} /> {t('Open with K-Desktop')}
        </div>
      </div>
      <Sfx at={p.cue('open') - 8} name="pop" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Chapter «Without code». */
export const NoCode: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const at = (id: string) => B(id).from - 6;
  return (
    <AbsoluteFill>
      <Part from={0} to={at('anims')} first><Files /></Part>
      <Part from={at('anims')} to={at('moves')}><Anims /></Part>
      <Part from={at('moves')} to={at('pack')}><Moves /></Part>
      <Part from={at('pack')} to={durationInFrames} last><Pack /></Part>
    </AbsoluteFill>
  );
};
