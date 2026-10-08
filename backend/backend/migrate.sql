CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE TABLE "Users" (
        "Id" uuid NOT NULL,
        "Email" text NOT NULL,
        "PasswordHash" text NOT NULL,
        "PlanType" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE TABLE "Forms" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Title" text NOT NULL,
        "PublicUrlSlug" text NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Forms" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Forms_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE TABLE "Subscriptions" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "StripeCustomerId" text NOT NULL,
        "Status" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Subscriptions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Subscriptions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE TABLE "Fields" (
        "Id" uuid NOT NULL,
        "FormId" uuid NOT NULL,
        "Label" text NOT NULL,
        "Type" integer NOT NULL,
        "IsRequired" boolean NOT NULL,
        "Order" integer NOT NULL,
        "Discriminator" character varying(13) NOT NULL,
        "Options" text[],
        "Min" numeric,
        "Max" numeric,
        "MaxLength" integer,
        CONSTRAINT "PK_Fields" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Fields_Forms_FormId" FOREIGN KEY ("FormId") REFERENCES "Forms" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE TABLE "FormResponses" (
        "Id" uuid NOT NULL,
        "FormId" uuid NOT NULL,
        "SubmittedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_FormResponses" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FormResponses_Forms_FormId" FOREIGN KEY ("FormId") REFERENCES "Forms" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE TABLE "Answers" (
        "Id" uuid NOT NULL,
        "FormResponseId" uuid NOT NULL,
        "FieldId" uuid NOT NULL,
        "Value" text NOT NULL,
        CONSTRAINT "PK_Answers" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Answers_Fields_FieldId" FOREIGN KEY ("FieldId") REFERENCES "Fields" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Answers_FormResponses_FormResponseId" FOREIGN KEY ("FormResponseId") REFERENCES "FormResponses" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE INDEX "IX_Answers_FieldId" ON "Answers" ("FieldId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE INDEX "IX_Answers_FormResponseId" ON "Answers" ("FormResponseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE INDEX "IX_Fields_FormId" ON "Fields" ("FormId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE INDEX "IX_FormResponses_FormId" ON "FormResponses" ("FormId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE INDEX "IX_Forms_UserId" ON "Forms" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    CREATE INDEX "IX_Subscriptions_UserId" ON "Subscriptions" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918203413_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260918203413_InitialCreate', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919134812_AddNameToUser') THEN
    ALTER TABLE "Users" ADD "Name" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919134812_AddNameToUser') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260919134812_AddNameToUser', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924144013_CurrentPeriodeEndSubscription') THEN
    ALTER TABLE "Subscriptions" ADD "CurrentPeriodEnd" timestamp with time zone NOT NULL DEFAULT TIMESTAMPTZ '-infinity';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924144013_CurrentPeriodeEndSubscription') THEN
    ALTER TABLE "Subscriptions" ADD "StripeSubscriptionId" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924144013_CurrentPeriodeEndSubscription') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260924144013_CurrentPeriodeEndSubscription', '10.0.12');
    END IF;
END $EF$;
COMMIT;

