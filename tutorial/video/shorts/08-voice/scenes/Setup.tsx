import React from 'react';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Sfx } from '../../../src/components/Sfx';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';

// Rows of the real Settings › Voice page (tutorial/captures/en/settings-voice.png).
export const VOICE_ROWS = {
  nav: [262, 590, 266, 48] as Focus['rect'],
  on: [572, 136, 960, 40] as Focus['rect'],
  models: [572, 188, 960, 42] as Focus['rect'],
  memory: [572, 240, 960, 42] as Focus['rect'],
  mic: [572, 292, 960, 42] as Focus['rect'],
  talk: [572, 424, 960, 42] as Focus['rect'],
  dictate: [572, 476, 960, 42] as Focus['rect'],
  dictateVoice: [572, 528, 960, 42] as Focus['rect'],
  echo: [572, 740, 960, 42] as Focus['rect'],
  privacy: [580, 898, 942, 46] as Focus['rect'],
};

/** Chapter «Set it up»: the real Settings › Voice page, row by row as the narrator names them. */
export const Setup: React.FC = () => {
  const B = useBeats();
  const models = B('models');
  const mic = B('mic');
  const priv = B('private');
  const R = VOICE_ROWS;
  const focus: Focus[] = [
    { from: models.cue('settings') - 4, to: models.cue('on') - 4, rect: R.nav },
    { from: models.cue('on') - 4, to: models.cue('download') - 4, rect: R.on },
    { from: models.cue('download') - 4, to: models.cue('free') - 4, rect: R.models, label: t('Downloaded once'), at: 'top' },
    { from: models.cue('free') - 4, to: mic.from, rect: R.memory },
    { from: mic.cue('mic') - 4, to: mic.cue('hotkey') - 4, rect: R.mic },
    { from: mic.cue('hotkey') - 4, to: mic.cue('echo') - 6, rect: R.talk },
    { from: mic.cue('echo') - 6, to: priv.from, rect: R.echo },
    { from: priv.cue('private') - 4, to: priv.to + 30, rect: R.privacy },
  ];
  return (
    <>
      <CaptureShot name="settings-voice" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 40, x: 760, y: 400, s: 1.15, rx: 0 },
          { at: models.cue('on') - 16, dur: 26, x: 1050, y: 260, s: 1.5 },
          { at: mic.cue('hotkey') - 18, dur: 26, x: 1050, y: 440, s: 1.45 },
          { at: mic.cue('echo') - 18, dur: 26, x: 1050, y: 720, s: 1.45 },
          { at: priv.from - 10, dur: 30, x: 1050, y: 800, s: 1.3 },
        ]} />
      {focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />)}
    </>
  );
};
