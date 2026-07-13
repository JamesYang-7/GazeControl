# MotionData

Motion files for data-driven agent animation (body motion and gaze patterns) go here.

`.npz` and `.wav` files in this folder are **git-ignored by default** (datasets can
be large); only the two example takes are whitelisted in `.gitignore` and committed
via Git LFS (`trn_2023_v0_000_interloctr_000` and `trn_2023_v0_000_main-agent_000`,
each npz + wav). To commit another clip, add matching `!` whitelist lines to
`.gitignore`.
