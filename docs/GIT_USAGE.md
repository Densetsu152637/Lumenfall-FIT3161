# Git Usage Guidelines

This document outlines the required Git practices for maintaining code quality, collaboration, and consistency across the team.

---

## 🔀 Merge Request Rules

### M1.1 – Delete Source Branch After Merge

- Once a PR is merged, the source branch **must be deleted**.
- If not done, the **System Architect (SA)** will contact the contributor to remove it.

---

### M1.2 – Use Pull Request Template

- Every PR **must follow the provided template** and clearly describe:
  - What was changed
  - Why it was changed
  - Any relevant context

- If incomplete, the **SA** will act to use it and request improvements.

---

### M1.3 – Ensure CI/CD Passes

- All PRs must pass **GitHub Actions (CI/CD checks)** before merging.
- PRs with failing checks will be **rejected**.

---

### M1.4 – No Self-Merging

- You **cannot merge your own PR**.
- PRs must be reviewed and approved by someone else.
- Self-merged PRs will be **rejected by the SA**.

---

## 🌿 Branching Rules

### B1.1 – Naming Conventions

Branches must follow the correct naming format:

| Type     | Prefix      | Example                   |
| -------- | ----------- | ------------------------- |
| Feature  | `feat/`     | `feat/login_page`         |
| Bug Fix  | `fix/`      | `fix/metrics_bug`         |
| Temp     | `temp/`     | `temp/form_validation`    |
| Chore    | `chore/`    | `chore/update_eslint`     |
| Docs     | `docs/`     | `docs/api_docs`           |
| Style    | `style/`    | `style/header_formatting` |
| Refactor | `refactor/` | `refactor/auth_flow`      |
| Test     | `test/`     | `test/user_service_tests` |
| CI/CD    | `ci/`       | `ci/add_cache_step`       |

- Incorrect naming → contributor will be asked to rename the branch/commit.

---

### B1.3 – Do Not Commit to Others’ Branches

- Never push changes directly to another person’s branch without permission.
- Instead:
  - Create your own branch
  - Submit a PR

- Violations will result in SA intervention.

---

## 🧾 Commit Rules

### C1.1 – Conventional Commit Format

Commits must follow the correct naming format:

| Type     | Prefix      | Example                                   |
| -------- | ----------- | ----------------------------------------- |
| Feature  | `feat:`     | `feat: implementing login page`           |
| Bug Fix  | `fix:`      | `fix: metrics Nan bug`                    |
| Temp     | `temp:`     | `temp: form validation`                   |
| Chore    | `chore:`    | `chore: updating eslint to reflect docs`  |
| Docs     | `docs:`     | `docs: adding API docs`                   |
| Style    | `style:`    | `style: changing typefont to []`          |
| Refactor | `refactor:` | `refactor: simplifying metrics functions` |
| Test     | `test:`     | `test: implementing user testcases`       |
| CI/CD    | `ci:`       | `ci: adding cache step`                   |

- Incorrect commit formats → contributor must **rebase or squash commits**.

---

### C1.2 – Keep Commits Clean

- Commits must be:
  - Short
  - Clear
  - Focused on a single change

- Avoid:
  - Large, vague commits
  - Mixing unrelated changes

If unclear, you will be asked to:

- Split commits
- Rewrite commit messages

---

## ✅ Summary Checklist

Before submitting a PR, ensure:

- [ ] Branch follows naming conventions
- [ ] Commits follow conventional format
- [ ] Commits are clean and meaningful
- [ ] PR template is fully completed
- [ ] CI/CD checks pass
- [ ] PR is reviewed by at least one SA
- [ ] You are **not merging your own PR**

---

## 🚨 Enforcement

Failure to follow these rules may result in:

- PR rejection
- Requests for changes
- Additional review delays

---

Following these guidelines ensures:

- Better collaboration 🤝
- Cleaner project history 📚
- Higher code quality 🚀

---
