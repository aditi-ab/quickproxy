# Security policy

Do not report vulnerabilities through a public issue. Use GitHub private vulnerability reporting for this repository.

Never attach credentials, access tokens, private keys, certificates, production configuration, customer data, or internal hostnames to an issue. Supply encryption keys, certificate passwords, identity-provider secrets, and database credentials through environment variables or a deployment secret store.

Generated databases, data-protection keys, and certificate material belong under ignored runtime data directories and must not be committed.
