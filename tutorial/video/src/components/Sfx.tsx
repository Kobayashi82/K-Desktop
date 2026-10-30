import React from 'react';
import { Audio, Sequence, staticFile } from 'remotion';

export type SfxName =
  | 'whoosh' | 'whoosh-short' | 'whoosh-down' | 'click' | 'key' | 'pop' | 'pop-low' | 'pop-high'
  | 'chime' | 'rise' | 'impact' | 'blip' | 'blip-low' | 'achievement';

/** A sound effect at a frame of the current sequence. */
export const Sfx: React.FC<{ at: number; name: SfxName; volume?: number }> = ({ at, name, volume = 0.5 }) => (
  <Sequence from={Math.round(at)} durationInFrames={90} layout="none">
    <Audio src={staticFile(`sfx/${name}.wav`)} volume={volume} />
  </Sequence>
);
