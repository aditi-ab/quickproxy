# Writing style

- Do not use em dashes (`—`) in literal user-facing text. Use commas, parentheses, colons, or separate sentences instead.

# Customer-facing product documentation

- Write product documentation for customers who use, administer, deploy, or integrate with QuickProxy.
- Include technical details only when customers need them to install, configure, secure, operate, troubleshoot, or consume a supported capability.
- Do not include internal implementation details, source-code structure, development workflows, CI/CD mechanics, build-pipeline behavior, internal architecture notes, or contributor instructions in `documentation/`.
- Keep internal and contributor material outside the customer-facing VitePress documentation. Do not describe planned or unreleased behavior as available to customers.

# Product documentation is part of every change

Whenever product behavior, configuration, public contracts, APIs, schemas, security behavior, deployment, operations, or user interface changes, update the matching VitePress pages under `documentation/` in the same change. Add or update navigation when a page is added, removed, or renamed.

Before completing a product change, compare the changed files with the documentation, run the relevant application tests, and run `yarn docs:build` from `documentation`. Do not describe planned behavior as implemented behavior. Keep examples free of real credentials, internal hostnames, secrets, and sensitive data.
