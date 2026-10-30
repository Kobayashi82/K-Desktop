import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../../../src/components/Chapter';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { Osd } from '../../../src/components/Osd';
import { Sfx } from '../../../src/components/Sfx';
import { Callout } from '../../../src/components/Ui';
import { ease, inOut, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { BeatTimes, useBeats, useChapter } from '../../../src/lib/timeline';
import { SpacePart, WordsPart } from '../../../src/scenes/voice/Match';
import { Chain, Media } from '../../../src/scenes/voice/Uses';
import { C, SANS } from '../../../src/theme';

/** «Listening…» while the narrator says to hold the hotkey, before the words are taken apart. */
const Listening: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  return (
    <div style={{ position: 'absolute', left: 960, top: 520, transform: 'translateX(-50%)' }}>
      <Osd icon="mic_24_filled" color="teal" text={t('Listening…')} show={inOut(f, b.from, b.cue('finding') - 6, 8)} />
    </div>
  );
};

/** Not sure: it shows what it heard and its best guess («“…”: not sure (“…”?)»), and runs nothing. */
const UnsurePart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const heard = pop(f, b.from + 4, { damping: 14 });
  const osd = inOut(f, b.cue('shows') - 6, b.to + 40, 8);
  const guess = ease(f, b.cue('thinks') - 4, b.cue('thinks') + 8);
  const none = pop(f, b.cue('nothing') - 4, { damping: 11, stiffness: 160 });
  return (
    <AbsoluteFill>
      <div style={{
        position: 'absolute', left: 960, top: 300, transform: `translate(-50%, -50%) scale(${0.8 + 0.2 * heard})`, opacity: Math.min(1, heard * 1.4),
        padding: '22px 38px', borderRadius: 34, background: 'rgba(30,30,36,0.95)', border: `1px solid ${C.line2}`, display: 'flex', alignItems: 'center', gap: 18,
      }}>
        <KdIcon icon="mic_24_filled" color="teal" size={56} />
        <span style={{ fontFamily: SANS, fontSize: 46, fontWeight: 700, color: C.text }}>“{t('empty the bean')}”</span>
      </div>
      <div style={{ position: 'absolute', left: 960, top: 520, transform: 'translateX(-50%)' }}>
        <Osd icon="mic_24_filled" color="teal" show={osd} scale={1.8}
          text={`“${t('empty the bean')}”: ${t('not sure')} (“${t('Empty Recycle Bin')}”?)`} />
      </div>
      <Callout show={guess * (1 - ease(f, b.cue('nothing') - 6, b.cue('nothing')))} x={960} y={680} align="center" size={28}>{t('Its best guess')}</Callout>
      <div style={{
        position: 'absolute', left: 960, top: 760, transform: `translate(-50%, -50%) scale(${none})`, display: 'flex', alignItems: 'center', gap: 16,
        padding: '16px 34px', borderRadius: 40, background: 'rgba(255,107,107,0.14)', border: `2px solid ${C.danger}`,
      }}>
        <Fluent name="dismiss_circle_24_filled" size={44} color={C.danger} />
        <span style={{ fontFamily: SANS, fontSize: 38, fontWeight: 800, color: C.text }}>{t('Nothing runs')}</span>
      </div>
      <Sfx at={b.from + 4} name="pop" volume={0.22} />
      <Sfx at={b.cue('shows') - 6} name="blip-low" volume={0.25} />
      <Sfx at={b.cue('nothing') - 4} name="impact" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Chapter «Say a command»: the long video's word and meaning layers, then «not sure», chains, and music by voice. */
export const Commands: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const match = B('match');
  const meaning = B('meaning');
  const unsure = B('unsure');
  const chain = B('chain');
  const media = B('media');
  return (
    <AbsoluteFill>
      <Part from={0} to={meaning.from - 6} first>
        <WordsPart match={match} words={B('words')} />
        <Listening b={match} />
      </Part>
      <Part from={meaning.from - 6} to={unsure.from - 6}><SpacePart meaning={meaning} meaning2={B('meaning2')} notmine={B('notmine')} /></Part>
      <Part from={unsure.from - 6} to={chain.cue('open') - 14}><UnsurePart b={unsure} /></Part>
      <Part from={chain.cue('open') - 14} to={media.from - 6}><Chain b={chain} /></Part>
      <Part from={media.from - 6} to={durationInFrames} last><Media b={media} /></Part>
    </AbsoluteFill>
  );
};
