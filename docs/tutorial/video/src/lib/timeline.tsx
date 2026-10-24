import React, { createContext, useContext } from 'react';

/** What tools/tts.py writes in public/generated/timeline.<lang>.json (frames; a beat's are relative to its chapter). */
export type Segment = { text: string; from: number; to: number };
export type Beat = { id: string; text: string; audio: string; from: number; durationInFrames: number; segments: Segment[]; cues?: Record<string, number> };
export type Chapter = { id: string; title: string; label: string; from: number; durationInFrames: number; beats: Beat[] };
export type Cue = { from: number; to: number; text: string };
export type Timeline = { lang: string; fps: number; durationInFrames: number; chapters: Chapter[]; subtitles?: Cue[] };

const ChapterContext = createContext<Chapter | null>(null);

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
