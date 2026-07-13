# MotionData

Motion files for data-driven agent animation (body motion and gaze patterns) go here.

`.npz` files in this folder are **git-ignored by default** (datasets can be large);
only two example clips are whitelisted in `.gitignore` and committed via Git LFS
(`trn_2023_v0_000_interloctr_000.npz`, `trn_2023_v0_000_main-agent_000.npz`).
To commit another clip, add a matching `!` whitelist line to `.gitignore`.
