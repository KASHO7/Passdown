Version: 0.1.0-rc.15
Title: Weather and pricing accuracy
Date: 2026-11-28
---
## Weather Information

- Weather comes from the U.S. National Weather Service for Carowinds' fixed location, covering both the North Carolina and South Carolina sides of the park. The app never uses your computer's location.
- The Dashboard has a Weather card: nearby station conditions (named by station and time), today's forecast high and low, the rain chance during park hours, and any official NWS alerts in effect. Refresh Weather is in the expanded weather details.
- The Calendar shows a compact forecast for the selected date, with an hourly breakdown you can expand. Weather is never added to the month grid.
- Weather is checked only while the app is open, and selecting dates or moving around the app never goes online. The last weather received is shown when you are offline and is marked when it may be out of date.
- Weather is for information only. It does not replace official warnings or park procedures.

## Passdown and PDF

- Passdown PDFs (Classic, Compact and Modern) can include a small Weather Outlook for the date's park hours. It is left out when no current forecast covers those hours, and it never shows alerts.
- Choose it with "Weather Outlook" under Overview > Include in Passdown, or with "Include Weather Outlook by default" in Settings > Passdown Defaults. Both are the same setting, saved on this computer only.
- Weather in printed Passdowns. Print, Save PDF and Create PDF always use the latest weather the app has. If new weather arrives while the preview is open, the printed or saved PDF can show newer weather than the preview did; the preview updates to match right after.

## Pricing Accuracy

- After the park closes, the official sales calendar stops listing that day. Daily Admission, Fast Lane and other dated ticket prices that were already verified for the day are now kept instead of disappearing, with their original verification time.
- These prices are labelled as previously verified on Products & Pricing, the Dashboard and the PDF, and in the Calendar as "last verified" with the date. This is the last price Carowinds published for that day, not a sign that tickets are still on sale.
- Display-price overrides keep working as before.

## Reliability

- Internal maintenance and testing improvements, including a simpler Early Ride Time editor behind the same screen.

## Compatibility

- This version does not change the database: coming from rc.11, rc.12, rc.13 or rc.14, nothing is upgraded. The Personal Sync format is unchanged.
- Weather and the Weather Outlook setting stay on this computer: they are not shared through Personal Sync and are not added to drafts or finalized History. History still opens the original stored PDF.

Your drafts, notes, History PDFs, prices, display-price overrides, Personal Sync settings and appearance choice are kept.
