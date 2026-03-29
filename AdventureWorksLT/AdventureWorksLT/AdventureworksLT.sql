-- AdventureWorks Database Schema
-- Generated: 2026-03-11 23:28:12

USE [AdventureWorks]
GO

CREATE TABLE [dbo].[BuildVersion] (
    [SystemInformationID] TINYINT IDENTITY(1,1) NOT NULL,
    [Database Version] NVARCHAR(25) NOT NULL,
    [VersionDate] DATETIME NOT NULL,
    [ModifiedDate] DATETIME DEFAULT (getdate()) NOT NULL
);
GO

CREATE TABLE [dbo].[ErrorLog] (
    [ErrorLogID] INT IDENTITY(1,1) NOT NULL,
    [ErrorTime] DATETIME DEFAULT (getdate()) NOT NULL,
    [UserName] NVARCHAR(128) NOT NULL,
    [ErrorNumber] INT NOT NULL,
    [ErrorSeverity] INT NULL,
    [ErrorState] INT NULL,
    [ErrorProcedure] NVARCHAR(126) NULL,
    [ErrorLine] INT NULL,
    [ErrorMessage] NVARCHAR(4000) NOT NULL,
    CONSTRAINT [PK_ErrorLog_ErrorLogID] PRIMARY KEY ([ErrorLogID])
);
GO

CREATE TABLE [SalesLT].[Address] (
    [AddressID] INT IDENTITY(1,1) NOT NULL,
    [AddressLine1] NVARCHAR(60) NOT NULL,
    [AddressLine2] NVARCHAR(60) NULL,
    [City] NVARCHAR(30) NOT NULL,
    [StateProvince] NVARCHAR(50) NOT NULL,
    [CountryRegion] NVARCHAR(50) NOT NULL,
    [PostalCode] NVARCHAR(15) NOT NULL,
    [rowguid] UNIQUEIDENTIFIER DEFAULT (newid()) NOT NULL,
    [ModifiedDate] DATETIME DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_Address_AddressID] PRIMARY KEY ([AddressID])
);
GO

CREATE TABLE [SalesLT].[Customer] (
    [CustomerID] INT IDENTITY(1,1) NOT NULL,
    [NameStyle] BIT DEFAULT ((0)) NOT NULL,
    [Title] NVARCHAR(8) NULL,
    [FirstName] NVARCHAR(50) NOT NULL,
    [MiddleName] NVARCHAR(50) NULL,
    [LastName] NVARCHAR(50) NOT NULL,
    [Suffix] NVARCHAR(10) NULL,
    [CompanyName] NVARCHAR(128) NULL,
    [SalesPerson] NVARCHAR(256) NULL,
    [EmailAddress] NVARCHAR(50) NULL,
    [Phone] NVARCHAR(25) NULL,
    [PasswordHash] VARCHAR(128) NOT NULL,
    [PasswordSalt] VARCHAR(10) NOT NULL,
    [rowguid] UNIQUEIDENTIFIER DEFAULT (newid()) NOT NULL,
    [ModifiedDate] DATETIME DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_Customer_CustomerID] PRIMARY KEY ([CustomerID])
);
GO

CREATE TABLE [SalesLT].[CustomerAddress] (
    [CustomerID] INT NOT NULL,
    [AddressID] INT NOT NULL,
    [AddressType] NVARCHAR(50) NOT NULL,
    [rowguid] UNIQUEIDENTIFIER DEFAULT (newid()) NOT NULL,
    [ModifiedDate] DATETIME DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_CustomerAddress_CustomerID_AddressID] PRIMARY KEY ([CustomerID], [AddressID])
);
GO

CREATE TABLE [SalesLT].[Product] (
    [ProductID] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(50) NOT NULL,
    [ProductNumber] NVARCHAR(25) NOT NULL,
    [Color] NVARCHAR(15) NULL,
    [StandardCost] MONEY NOT NULL,
    [ListPrice] MONEY NOT NULL,
    [Size] NVARCHAR(5) NULL,
    [Weight] DECIMAL(8, 2) NULL,
    [ProductCategoryID] INT NULL,
    [ProductModelID] INT NULL,
    [SellStartDate] DATETIME NOT NULL,
    [SellEndDate] DATETIME NULL,
    [DiscontinuedDate] DATETIME NULL,
    [ThumbNailPhoto] VARBINARY(MAX) NULL,
    [ThumbnailPhotoFileName] NVARCHAR(50) NULL,
    [rowguid] UNIQUEIDENTIFIER DEFAULT (newid()) NOT NULL,
    [ModifiedDate] DATETIME DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_Product_ProductID] PRIMARY KEY ([ProductID])
);
GO

CREATE TABLE [SalesLT].[ProductCategory] (
    [ProductCategoryID] INT IDENTITY(1,1) NOT NULL,
    [ParentProductCategoryID] INT NULL,
    [Name] NVARCHAR(50) NOT NULL,
    [rowguid] UNIQUEIDENTIFIER DEFAULT (newid()) NOT NULL,
    [ModifiedDate] DATETIME DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_ProductCategory_ProductCategoryID] PRIMARY KEY ([ProductCategoryID])
);
GO

CREATE TABLE [SalesLT].[ProductDescription] (
    [ProductDescriptionID] INT IDENTITY(1,1) NOT NULL,
    [Description] NVARCHAR(400) NOT NULL,
    [rowguid] UNIQUEIDENTIFIER DEFAULT (newid()) NOT NULL,
    [ModifiedDate] DATETIME DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_ProductDescription_ProductDescriptionID] PRIMARY KEY ([ProductDescriptionID])
);
GO

CREATE TABLE [SalesLT].[ProductModel] (
    [ProductModelID] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(50) NOT NULL,
    [CatalogDescription] XML NULL,
    [rowguid] UNIQUEIDENTIFIER DEFAULT (newid()) NOT NULL,
    [ModifiedDate] DATETIME DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_ProductModel_ProductModelID] PRIMARY KEY ([ProductModelID])
);
GO

CREATE TABLE [SalesLT].[ProductModelProductDescription] (
    [ProductModelID] INT NOT NULL,
    [ProductDescriptionID] INT NOT NULL,
    [Culture] NCHAR(6) NOT NULL,
    [rowguid] UNIQUEIDENTIFIER DEFAULT (newid()) NOT NULL,
    [ModifiedDate] DATETIME DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_ProductModelProductDescription_ProductModelID_ProductDescriptionID_Culture] PRIMARY KEY ([ProductModelID], [ProductDescriptionID], [Culture])
);
GO

CREATE TABLE [SalesLT].[SalesOrderDetail] (
    [SalesOrderID] INT NOT NULL,
    [SalesOrderDetailID] INT IDENTITY(1,1) NOT NULL,
    [OrderQty] SMALLINT NOT NULL,
    [ProductID] INT NOT NULL,
    [UnitPrice] MONEY NOT NULL,
    [UnitPriceDiscount] MONEY DEFAULT ((0.0)) NOT NULL,
    [LineTotal] NUMERIC(38, 6) NOT NULL,
    [rowguid] UNIQUEIDENTIFIER DEFAULT (newid()) NOT NULL,
    [ModifiedDate] DATETIME DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_SalesOrderDetail_SalesOrderID_SalesOrderDetailID] PRIMARY KEY ([SalesOrderID], [SalesOrderDetailID])
);
GO

CREATE TABLE [SalesLT].[SalesOrderHeader] (
    [SalesOrderID] INT IDENTITY(1,1) NOT NULL,
    [RevisionNumber] TINYINT DEFAULT ((0)) NOT NULL,
    [OrderDate] DATETIME DEFAULT (getdate()) NOT NULL,
    [DueDate] DATETIME NOT NULL,
    [ShipDate] DATETIME NULL,
    [Status] TINYINT DEFAULT ((1)) NOT NULL,
    [OnlineOrderFlag] BIT DEFAULT ((1)) NOT NULL,
    [SalesOrderNumber] NVARCHAR(25) NOT NULL,
    [PurchaseOrderNumber] NVARCHAR(25) NULL,
    [AccountNumber] NVARCHAR(15) NULL,
    [CustomerID] INT NOT NULL,
    [ShipToAddressID] INT NULL,
    [BillToAddressID] INT NULL,
    [ShipMethod] NVARCHAR(50) NOT NULL,
    [CreditCardApprovalCode] VARCHAR(15) NULL,
    [SubTotal] MONEY DEFAULT ((0.00)) NOT NULL,
    [TaxAmt] MONEY DEFAULT ((0.00)) NOT NULL,
    [Freight] MONEY DEFAULT ((0.00)) NOT NULL,
    [TotalDue] MONEY NOT NULL,
    [Comment] NVARCHAR(MAX) NULL,
    [rowguid] UNIQUEIDENTIFIER DEFAULT (newid()) NOT NULL,
    [ModifiedDate] DATETIME DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_SalesOrderHeader_SalesOrderID] PRIMARY KEY ([SalesOrderID])
);
GO

-- Foreign Key Constraints

ALTER TABLE [SalesLT].[CustomerAddress]
    ADD CONSTRAINT [FK_CustomerAddress_Address_AddressID]
    FOREIGN KEY ([AddressID])
    REFERENCES [SalesLT].[Address] ([AddressID]);
GO

ALTER TABLE [SalesLT].[CustomerAddress]
    ADD CONSTRAINT [FK_CustomerAddress_Customer_CustomerID]
    FOREIGN KEY ([CustomerID])
    REFERENCES [SalesLT].[Customer] ([CustomerID]);
GO

ALTER TABLE [SalesLT].[Product]
    ADD CONSTRAINT [FK_Product_ProductCategory_ProductCategoryID]
    FOREIGN KEY ([ProductCategoryID])
    REFERENCES [SalesLT].[ProductCategory] ([ProductCategoryID]);
GO

ALTER TABLE [SalesLT].[Product]
    ADD CONSTRAINT [FK_Product_ProductModel_ProductModelID]
    FOREIGN KEY ([ProductModelID])
    REFERENCES [SalesLT].[ProductModel] ([ProductModelID]);
GO

ALTER TABLE [SalesLT].[ProductCategory]
    ADD CONSTRAINT [FK_ProductCategory_ProductCategory_ParentProductCategoryID_ProductCategoryID]
    FOREIGN KEY ([ParentProductCategoryID])
    REFERENCES [SalesLT].[ProductCategory] ([ProductCategoryID]);
GO

ALTER TABLE [SalesLT].[ProductModelProductDescription]
    ADD CONSTRAINT [FK_ProductModelProductDescription_ProductDescription_ProductDescriptionID]
    FOREIGN KEY ([ProductDescriptionID])
    REFERENCES [SalesLT].[ProductDescription] ([ProductDescriptionID]);
GO

ALTER TABLE [SalesLT].[ProductModelProductDescription]
    ADD CONSTRAINT [FK_ProductModelProductDescription_ProductModel_ProductModelID]
    FOREIGN KEY ([ProductModelID])
    REFERENCES [SalesLT].[ProductModel] ([ProductModelID]);
GO

ALTER TABLE [SalesLT].[SalesOrderDetail]
    ADD CONSTRAINT [FK_SalesOrderDetail_Product_ProductID]
    FOREIGN KEY ([ProductID])
    REFERENCES [SalesLT].[Product] ([ProductID]);
GO

ALTER TABLE [SalesLT].[SalesOrderDetail]
    ADD CONSTRAINT [FK_SalesOrderDetail_SalesOrderHeader_SalesOrderID]
    FOREIGN KEY ([SalesOrderID])
    REFERENCES [SalesLT].[SalesOrderHeader] ([SalesOrderID]);
GO

ALTER TABLE [SalesLT].[SalesOrderHeader]
    ADD CONSTRAINT [FK_SalesOrderHeader_Address_BillTo_AddressID]
    FOREIGN KEY ([BillToAddressID])
    REFERENCES [SalesLT].[Address] ([AddressID]);
GO

ALTER TABLE [SalesLT].[SalesOrderHeader]
    ADD CONSTRAINT [FK_SalesOrderHeader_Address_ShipTo_AddressID]
    FOREIGN KEY ([ShipToAddressID])
    REFERENCES [SalesLT].[Address] ([AddressID]);
GO

ALTER TABLE [SalesLT].[SalesOrderHeader]
    ADD CONSTRAINT [FK_SalesOrderHeader_Customer_CustomerID]
    FOREIGN KEY ([CustomerID])
    REFERENCES [SalesLT].[Customer] ([CustomerID]);
GO

