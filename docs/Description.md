## SkipWatch
Skip or watch. SkipWatch is a single-user, locally installed application that turns the YouTube channels you follow into a calm, triaged, searchable library. Deterministic collectors fetch new videos on a schedule. An LLM summarizes each one into a 1–2 paragraph decision-support summary. You file them into Libraries, group them into Projects, or hit Pass — and your dashboard stays clean.

Designed to run on your own machine. Single user. No accounts, no cloud, no notifications.

## What it does
- Collects new videos from YouTube channels on a schedule (YouTube Data API + Apify)
- Summarizes each video into a decision-support filter (1–2 paragraphs, lead with the subject matter — no fluff)
- Triages with three first-class actions on every video card:
- Library ▾ — file into a consumption bucket ("Education", "Entertainment"). Hides from the main feed.
- Project ▾ — group into research collections ("AI Skills"). Stays visible in the main feed. Allow you to create guides or reports based on the videos in the project.
- Pass — dismiss. Hidden but recoverable.
- Searches the whole library with FTS5 and a smart-search chat that returns cited answers
- Synthesizes rollups across multiple videos when you ask for synthesis in chat

## What it is not
- Not a personal AI assistant
- Not a multi-source aggregator (no RSS / podcasts / papers — different product)
- Not a Telegram bot or mobile app
- Not a general chat product — chat is scoped to your video library