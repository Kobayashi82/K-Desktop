import React from 'react';
import { AbsoluteFill, Audio, Sequence, staticFile, useCurrentFrame } from 'remotion';
import { Background, Grain } from './components/Background';
import { CaptureInfo, MediaProvider } from './components/Capture';
import { ChapterStage, ChapterTitle, LightSweep, ProgressBar } from './components/Chapter';
import { setLang } from './lib/i18n';
import { ChapterProvider, Timeline } from './lib/timeline';
import { SCENES } from './scenes';

export type VideoProps = { lang: string; timeline?: Timeline; captures?: Record<string, CaptureInfo>; music?: boolean };

// Frames in public/sfx/music.wav (exactly 4 minutes). It loops seamlessly, so the copies go back to back, with no
// cross-fade: a fade there dipped the music and the beat restarted out of step.
const MUSIC_LOOP = 30 * 60 * 4;

/** Frames where the narrator speaks (the music steps back there). */
const speaking = (timeline: Timeline, frame: number): number => {
  let near = 1e9;
  for (const ch of timeline.chapters)
    for (const b of ch.beats) {
      const a = ch.from + b.from;
      const z = a + b.durationInFrames;
      if (frame >= a && frame <= z) return 1;
      near = Math.min(near, Math.abs(frame - a), Math.abs(frame - z));
    }
  return Math.max(0, 1 - near / 20);
};

const Music: React.FC<{ timeline: Timeline }> = ({ timeline }) => {
  const total = timeline.durationInFrames;
  const loops = Math.ceil(total / MUSIC_LOOP);
  return (
    <>
      {Array.from({ length: loops }, (_, i) => {
        const from = i * MUSIC_LOOP;
        return (
          <Sequence key={i} from={from} durationInFrames={Math.min(MUSIC_LOOP, total - from)} layout="none">
            <Audio src={staticFile('sfx/music.wav')} volume={f => {
              const global = from + f;
              const fade = Math.min(1, global / 45, (total - global) / 90);
              const duck = 1 - 0.55 * speaking(timeline, global);
              return 0.34 * Math.max(0, fade) * duck;
            }} />
          </Sequence>
        );
      })}
    </>
  );
};

export const Video: React.FC<VideoProps> = ({ lang, timeline, captures = {}, music = true }) => {
  const frame = useCurrentFrame();
  setLang(lang);
  if (!timeline) return null;
  return (
    <MediaProvider lang={lang} captures={captures}>
    <AbsoluteFill style={{ background: '#0b0b0e' }}>
      <Background timeline={timeline} />
      {timeline.chapters.map((chapter, index) => {
        const Scene = SCENES[chapter.id];
        const titled = index > 0 && index < timeline.chapters.length - 1;
        return (
          <Sequence key={chapter.id} from={chapter.from} durationInFrames={chapter.durationInFrames} name={chapter.id}>
            <ChapterProvider chapter={chapter}>
              <ChapterStage>{Scene ? <Scene /> : null}</ChapterStage>
              {titled && <ChapterTitle index={index} />}
              {chapter.beats.map(beat => (
                <Sequence key={beat.id} from={beat.from} durationInFrames={beat.durationInFrames + 2} layout="none" name={`🗣 ${beat.id}`}>
                  <Audio src={staticFile(beat.audio)} />
                </Sequence>
              ))}
            </ChapterProvider>
          </Sequence>
        );
      })}
      {timeline.chapters.slice(1).map(ch => <LightSweep key={ch.id} at={ch.from + 9} frame={frame} />)}
      <ProgressBar chapters={timeline.chapters} frame={frame} />
      {music && <Music timeline={timeline} />}
      <Grain />
    </AbsoluteFill>
    </MediaProvider>
  );
};
