-- Development schema for disposable tests. ID sizes and note constraints match the observed catalog.
CREATE TABLE dbo.Notes (
    ID varchar(255) NOT NULL PRIMARY KEY,
    Creator varchar(255) NOT NULL,
    CreationDate datetime2 NOT NULL,
    NoteText varchar(max) NOT NULL
);

CREATE TABLE dbo.NoteViews (
    ID int IDENTITY PRIMARY KEY,
    NoteID varchar(255) NOT NULL,
    ViewerIP varchar(50) NOT NULL,
    ViewTime datetime2 NOT NULL
);

CREATE TABLE dbo.NoteViewRequestHeaders (
    Id int IDENTITY PRIMARY KEY,
    NoteId nvarchar(64) NOT NULL,
    Name sysname NOT NULL,
    Value nvarchar(max) NOT NULL
);

CREATE TABLE dbo.Links (
    ShortURL varchar(50) NOT NULL PRIMARY KEY,
    Creator varchar(255) NOT NULL,
    CreationDate datetime NOT NULL,
    URL varchar(max) NOT NULL,
    ModifiedDate datetime NULL
);

CREATE TABLE dbo.LinkVisits (
    VisitID int IDENTITY PRIMARY KEY,
    ShortURL varchar(50) NOT NULL REFERENCES dbo.Links(ShortURL),
    VisitorIP varchar(50) NOT NULL,
    VisitTime datetime NOT NULL,
    Referrer varchar(max) NULL
);
