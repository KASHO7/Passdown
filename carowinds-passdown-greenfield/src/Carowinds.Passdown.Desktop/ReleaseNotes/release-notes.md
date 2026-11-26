Version: 0.1.0-rc.13
Title: Faster preview, faster PDFs and a smaller download
Date: 2026-11-22
---
## Faster Live PDF Preview

- The Passdown preview now updates almost immediately after an individual change, such as selecting a product or a show or ticking a checkbox.
- Continuous typing is handled efficiently: the preview refreshes once more shortly after you pause.
- Switching dates shows the new date's preview sooner.

## Faster PDF Startup

- The app prepares the PDF engine in the background after it opens. Once that has finished, the first preview, Create PDF or Print starts without the earlier extra delay.

## Faster Price History

- Switching between products in Season Price History is quicker, even with a lot of synced price history.

## More Efficient Official Refreshes

- When Check Online or the automatic refresh asks the official ticket calendar the same question more than once, the request is sent once and the answer is shared. Each product still reads its own date and status from that answer.

## Lower Memory Use

- The PDF preview no longer creates a large temporary copy of each page image.

## Smaller Download

- Unused embedded fonts and an unused image were removed. The Windows ZIP is about 5 MB smaller than rc.12. PDFs look and print exactly as before.

## Maintenance and Reliability

- The official ticket-calendar sources share simpler common code, with more automated checks behind them.

## Compatibility

- Your Passdown drafts, Notes, Price History, Personal Sync, Early Ride Time and PDF styles work as before. This version does not change the database: coming from rc.11 or rc.12, nothing is upgraded.
- Pass Perks still show the program Carowinds publicly advertises. They do not mean a particular guest has earned or received a reward.

Your drafts, notes, History PDFs, Personal Sync settings and appearance choice are kept.
