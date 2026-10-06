# Contoso Advisors Billing Overview

## Purpose

This document describes how Contoso Advisors bills its clients on the TAMP platform. Contoso Advisors is a small independent advisory firm with a concentrated book of households, most of which pay a flat annual fee rather than an asset-based percentage. Because the client base is small, the operations team handles every billing run manually and reviews each invoice before release. This overview explains the firm's default schedule, the billing frequency, and the handful of exceptions that the operations team needs to know about. Internal reference for audit binders: CONTOSO-CANARY-2290.

## Billing Model

### Flat-Fee Clients

The majority of Contoso Advisors households are billed under the CONTOSO-FLAT-100 schedule. Under this schedule each household pays a fixed annual advisory fee that is divided into four equal quarterly installments. The fee does not change with market value, which makes client conversations about billing predictable and simple. A small number of legacy clients remain on an asset-based schedule, and those households are flagged in the client profile so that the operations team can review them separately during each quarterly run.

### Quarterly in Advance

Contoso Advisors bills quarterly in advance. The invoice for a quarter is generated on the first business day of that quarter and covers the three months that follow. Advance billing means that new accounts and terminations require proration, because the client may have paid for days on which the firm did not manage the assets. Refunds for terminated relationships are issued as billing credits in the next run.

## Ownership

The Contoso Advisors operations lead owns the billing calendar and approves every run before invoices are released. Advisors can view invoices for their own households but cannot change fee schedules. Only a FIRM_ADMIN user at Contoso Advisors can create or edit schedules on the platform.
