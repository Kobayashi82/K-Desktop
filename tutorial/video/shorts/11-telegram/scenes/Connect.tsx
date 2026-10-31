import React from 'react';
import { AbsoluteFill } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Sfx } from '../../../src/components/Sfx';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';

const blips = (focus: Focus[]) => focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />);

/** Settings › Accounts › Telegram Bot (telegram-bot.png): the bot and its guide. */
const Bot: React.FC = () => {
  const B = useBeats();
  const b = B('bot');
  const focus: Focus[] = [
    { from: b.cue('bot') - 4, to: b.cue('guide') - 4, rect: [564, 80, 976, 160] },
    { from: b.cue('guide') - 4, to: b.cue('guide') + 30, rect: [580, 212, 118, 14], label: t('How to create the bot'), at: 'right' },
  ];
  return (
    <>
      <CaptureShot name="telegram-bot" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: b.cue('bot') - 16, dur: 30, x: 1050, y: 250, s: 1.6 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** «Create your Telegram bot» (guide-telegram-bot.png): BotFather, the token, linking yourself. */
const Guide: React.FC = () => {
  const B = useBeats();
  const b = B('bot');
  const focus: Focus[] = [
    { from: b.cue('botfather') - 4, to: b.cue('paste') - 4, rect: [16, 84, 568, 86] },
    { from: b.cue('paste') - 4, to: b.cue('link') - 4, rect: [16, 318, 568, 120] },
    { from: b.cue('link') - 4, to: b.to + 30, rect: [16, 456, 568, 64] },
  ];
  return (
    <>
      <CaptureShot name="guide-telegram-bot" frame focus={focus}
        shots={[{ at: b.cue('guide'), x: 300, y: 292, s: 1.25 }, { at: b.cue('guide') + 8, dur: 30, x: 300, y: 292, s: 1.4 }]} />
      {blips(focus)}
    </>
  );
};

/** Back on the bot page: the linked owner. */
const Owner: React.FC = () => {
  const B = useBeats();
  const b = B('bot');
  return (
    <>
      <CaptureShot name="telegram-bot" focus={[{ from: b.cue('only') - 6, to: b.to + 30, rect: [564, 258, 976, 178], label: t('It only listens to you'), at: 'bottom' }]}
        shots={[{ at: b.cue('only') - 12, x: 1050, y: 330, s: 1.35 }, { at: b.cue('only') - 8, dur: 26, x: 1050, y: 340, s: 1.5 }]} />
      <Sfx at={b.cue('only') - 6} name="blip" volume={0.12} />
    </>
  );
};

/** Settings › Accounts › Telegram (telegram-account.png): an account with its own number. */
const Account: React.FC = () => {
  const B = useBeats();
  const a = B('account');
  const focus: Focus[] = [
    { from: a.cue('account') - 4, to: a.cue('account') + 40, rect: [261, 850, 266, 30] },
    { from: a.cue('account') + 40, to: a.cue('first') - 4, rect: [578, 96, 960, 54] },
    { from: a.cue('first') - 4, to: a.cue('app') - 4, rect: [578, 128, 650, 22], label: t('It can write first'), at: 'bottom' },
    { from: a.cue('app') - 4, to: a.to + 30, rect: [580, 226, 188, 14], label: t('How to get the api_id and api_hash'), at: 'right' },
  ];
  return (
    <>
      <CaptureShot name="telegram-account" focus={focus}
        shots={[
          { at: a.from - 8, x: 960, y: 540, s: 1.0 },
          { at: a.cue('account') + 26, dur: 30, x: 1000, y: 220, s: 1.7 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** Chapter «A bot or an account». */
export const Connect: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const b = B('bot');
  const a = B('account');
  return (
    <AbsoluteFill>
      <Part from={0} to={b.cue('guide') + 6} first><Bot /></Part>
      <Part from={b.cue('guide') + 6} to={b.cue('only') - 10}><Guide /></Part>
      <Part from={b.cue('only') - 10} to={a.from - 4}><Owner /></Part>
      <Part from={a.from - 4} to={durationInFrames} last><Account /></Part>
    </AbsoluteFill>
  );
};
