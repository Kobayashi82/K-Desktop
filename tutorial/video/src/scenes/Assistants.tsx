import React from 'react';
import { AbsoluteFill } from 'remotion';
import { Part } from '../components/Chapter';
import { useBeats, useChapter } from '../lib/timeline';
import { ArchPart, BrainPart, TalkPart } from './assistants/Inside';
import { KnowledgePart, MemoryPart, PlugPart } from './assistants/Knows';
import { InstallPart, MeetPart } from './assistants/Meet';
import { GesturesPart, OwnToolsPart, RiskPart, ToolsPart } from './assistants/Skills';
import { VoicesPart } from './assistants/Voices';

/** Chapter: the assistants. */
export const Assistants: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const at = (id: string) => B(id).from - 6;
  return (
    <AbsoluteFill>
      <Part from={0} to={at('module')} first><MeetPart intro={B('intro')} cast={B('cast')} /></Part>
      <Part from={at('module')} to={at('arch')}><InstallPart module={B('module')} install={B('install')} /></Part>
      <Part from={at('arch')} to={at('talk')}><ArchPart b={B('arch')} /></Part>
      <Part from={at('talk')} to={at('brain')}><TalkPart b={B('talk')} /></Part>
      <Part from={at('brain')} to={at('voice')}><BrainPart brain={B('brain')} brain2={B('brain2')} /></Part>
      <Part from={at('voice')} to={at('tools')}><VoicesPart voice={B('voice')} voice2={B('voice2')} /></Part>
      <Part from={at('tools')} to={at('risk')}><ToolsPart b={B('tools')} /></Part>
      <Part from={at('risk')} to={at('gestures')}><RiskPart b={B('risk')} /></Part>
      <Part from={at('gestures')} to={at('own-tools')}><GesturesPart b={B('gestures')} /></Part>
      <Part from={at('own-tools')} to={at('memory')}><OwnToolsPart b={B('own-tools')} /></Part>
      <Part from={at('memory')} to={at('knowledge')}><MemoryPart b={B('memory')} /></Part>
      <Part from={at('knowledge')} to={at('commands')}><KnowledgePart b={B('knowledge')} /></Part>
      <Part from={at('commands')} to={durationInFrames} last><PlugPart commands={B('commands')} create={B('create')} /></Part>
    </AbsoluteFill>
  );
};
