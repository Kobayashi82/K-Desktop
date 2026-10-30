import React from 'react';
import { AbsoluteFill } from 'remotion';
import { Part } from '../components/Chapter';
import { useBeats, useChapter } from '../lib/timeline';
import { WordsPart, SpacePart } from './voice/Match';
import { PipelinePart } from './voice/Pipeline';
import { TalkPart } from './voice/Talk';
import { UsesPart } from './voice/Uses';
import { WizardPart } from './voice/Wizard';

/** Chapter: voice control, from the microphone to the command, and what it is used for. */
export const Voice: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const own = B('own');
  const echo = B('echo');
  const match = B('match');
  const meaning = B('meaning');
  const chain = B('chain');
  return (
    <AbsoluteFill>
      <Part from={0} to={own.from - 6} first><TalkPart intro={B('intro')} two={B('two')} wake={B('wake')} /></Part>
      <Part from={own.from - 6} to={echo.from - 6}><WizardPart own={own} /></Part>
      <Part from={echo.from - 6} to={match.from - 2}><PipelinePart echo={echo} detector={B('detector')} verify={B('verify')} vad={B('vad')} stt={B('stt')} /></Part>
      <Part from={match.from - 2} to={meaning.from - 6}><WordsPart match={match} words={B('words')} /></Part>
      <Part from={meaning.from - 6} to={chain.from - 6}><SpacePart meaning={meaning} meaning2={B('meaning2')} notmine={B('notmine')} /></Part>
      <Part from={chain.from - 6} to={durationInFrames} last>
        <UsesPart chain={chain} media={B('media')} dictate={B('dictate')} privacy={B('privacy')} end={durationInFrames} />
      </Part>
    </AbsoluteFill>
  );
};
