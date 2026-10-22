# Solitaire: design

A Renegade card game on the card kit ([renegade-cards](https://github.com/darren-rawlings/renegade-cards)),
the first to use its solitaire table layout.

## The game
Klondike, the classic patience. Seven piles of one to seven cards (top card up), the stock and
waste top left, four foundations top right. Build the foundations up by suit from ace to king;
build the piles down in alternating colours; a face-up run moves together; only a king fills an
empty pile. Turn the stock one card or three at a time, and go through it as often as you like.
Rules in `src/Core/Klondike.cs`; the computer's way of playing in `src/Core/Autoplay.cs` (tested:
it wins about 38% of random deals, drawing one).

## Feel
- The deck is shuffled in front of you, squared up on the stock and dealt row by row, face down,
  with the top cards turning over.
- Tap a card: it (and the cards on it) flies to the best place it can go: a foundation first,
  then a pile with cards in it, then an empty pile. A card that has nowhere to go shakes.
- Safe cards (aces, twos, and cards nothing could still need) go up by themselves; once every
  card is face up, the game finishes itself.
- Signature effect: the foundations catch fire when you win.

## Help
- **Undo** is free (no ad) and goes back as far as you like; one Undo takes back your move with
  any safe cards that went up after it.
- **Hint** lifts the cards of a good move and says what to try.
- When the stock goes round with nothing new, it says so and offers a new deal.

## Settings
- **Draw:** one card or three.
- **Deals:** any shuffle, or winnable only (deals the computer's player has won).
- **Safe cards go up by themselves:** on by default.

## Record
The score line shows the moves and "Won X of Y" (a deal counts as played once you move).

## Scope for version 1
Klondike with the above. Later: drag to move, Vegas scoring, a timer, daily deals, tutorial clips.

## Changes from Darren's notes (2026-10-22)
- Settings › Moving cards: drag and drop (the default; a tap then only moves an ace up) or tap to send.
- Winning sets off the Windows-style cascade after the fire: the cards bounce out of the foundations leaving trails.
- One soft sound per move (no mini-shuffle when drawing); every move is in the log for the whole deal.
