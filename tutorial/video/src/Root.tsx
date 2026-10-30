import React from 'react';
import { CalculateMetadataFunction, Composition, Sequence, staticFile } from 'remotion';
import { Timeline } from './lib/timeline';
import { SHORTS } from './shorts';
import { Video, VideoProps } from './Video';

const loadTimeline = async (lang: string, short?: string): Promise<Timeline | null> => {
  const response = await fetch(staticFile(`generated/${short ? `shorts/${short}/` : ''}timeline.${lang}.json`));
  return response.ok ? response.json() : null; // no narration yet: python tools/tts.py --lang <lang> [--short <id>]
};

/** The real screenshots there are (tools/assets.py lists them). */
const loadCaptures = async () => {
  const response = await fetch(staticFile('generated/captures.json'));
  return response.ok ? response.json() : {};
};

const whole: CalculateMetadataFunction<VideoProps> = async ({ props }) => {
  const timeline = await loadTimeline(props.lang, props.short);
  return timeline ? { durationInFrames: timeline.durationInFrames, props: { ...props, timeline, captures: await loadCaptures() } } : { durationInFrames: 1 };
};

/** One chapter alone, for previews: the same video, shifted to the chapter's first frame. */
type ChapterProps = VideoProps & { chapter: string };

const ChapterOnly: React.FC<ChapterProps> = props => {
  const ch = props.timeline?.chapters.find(c => c.id === props.chapter);
  return ch ? <Sequence from={-ch.from}><Video {...props} /></Sequence> : null;
};

const chapterMeta: CalculateMetadataFunction<ChapterProps> = async ({ props }) => {
  const timeline = await loadTimeline(props.lang, props.short);
  const ch = timeline?.chapters.find(c => c.id === props.chapter);
  return timeline && ch ? { durationInFrames: ch.durationInFrames, props: { ...props, timeline, captures: await loadCaptures() } } : { durationInFrames: 1 };
};

export const Root: React.FC = () => (
  <>
    {['en', 'es'].map(lang => (
      <Composition key={lang} id={`kdesktop-${lang}`} component={Video} calculateMetadata={whole}
        width={1920} height={1080} fps={30} durationInFrames={1} defaultProps={{ lang, music: true }} />
    ))}
    {Object.keys(SHORTS).flatMap(short => ['en', 'es'].map(lang => (
      <Composition key={`${short}-${lang}`} id={`short-${short}-${lang}`} component={Video} calculateMetadata={whole}
        width={1920} height={1080} fps={30} durationInFrames={1} defaultProps={{ lang, music: true, short }} />
    )))}
    <Composition id="chapter" component={ChapterOnly} calculateMetadata={chapterMeta}
      width={1920} height={1080} fps={30} durationInFrames={1} defaultProps={{ lang: 'en', chapter: 'intro', music: true }} />
  </>
);
