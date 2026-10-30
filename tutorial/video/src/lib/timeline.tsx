import React, { createContext, useContext } from 'react';

/** What tools/tts.py writes in public/generated/timeline.<lang>.json (frames; a beat's are relative to its chapter). */
export type Segment = { text: string; from: number; to: number };
export type Beat = { id: string; text: string; audio: string; from: number; durationInFrames: number; segments: Segment[]; cues?: Record<string, number> };
export type Chapter = { id: string; title: string; label: string; from: number; durationInFrames: number; beats: Beat[] };
/** A short video's own data (tools/tts.py --short): its number, title and the next video's title. */
export type ShortInfo = { id: string; number: number; title: string; next?: string; nextNumber?: number };
export type Timeline = { lang: string; fps: number; durationInFrames: number; chapters: Chapter[]; short?: ShortInfo };

const ChapterContext = createContext<Chapter | null>(null);
const TimelineContext = createContext<Timeline | null>(null);

/** The whole video's timeline, for the pieces that need more than their chapter (the short videos' cards). */
export const TimelineProvider: React.FC<{ timeline: Timeline; children: React.ReactNode }> = ({ timeline, children }) => (
  <TimelineContext.Provider value={timeline}>{children}</TimelineContext.Provider>
);

export const useTimeline = (): Timeline => {
  const timeline = useContext(TimelineContext);
  if (!timeline) throw new Error('useTimeline outside the video');
  return timeline;
};

export const ChapterProvider: React.FC<{ chapter: Chapter; children: React.ReactNode }> = ({ chapter, children }) => (
  <ChapterContext.Provider value={chapter}>{children}</ChapterContext.Provider>
);

export const useChapter = (): Chapter => {
  const chapter = useContext(ChapterContext);
  if (!chapter) throw new Error('useChapter outside a chapter');
  return chapter;
};

/**
 * Times of the chapter's beats, by id: start, end, the start and end of each sentence, and its cues: the moment the
 * word after a {name} marker of the script is said (estimated from where it sits in its sentence).
 */
export type BeatTimes = { from: number; to: number; dur: number; seg: (i: number) => number; segEnd: (i: number) => number; cue: (name: string) => number };

export const useBeats = (): ((id: string) => BeatTimes) => {
  const chapter = useChapter();
  return (id: string) => {
    const beat = chapter.beats.find(b => b.id === id);
    if (!beat) throw new Error(`No beat "${id}" in chapter "${chapter.id}"`);
    const segs = beat.segments;
    return {
      from: beat.from,
      to: beat.from + beat.durationInFrames,
      dur: beat.durationInFrames,
      seg: i => segs[Math.min(Math.max(i, 0), segs.length - 1)].from,
      segEnd: i => segs[Math.min(Math.max(i, 0), segs.length - 1)].to,
      cue: name => {
        const at = beat.cues?.[name];
        if (at === undefined) throw new Error(`No cue {${name}} in beat "${id}" of "${chapter.id}"`);
        return at;
      },
    };
  };
};
