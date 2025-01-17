﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using PrePaymentModule.Models;
using System.Data.Entity;
using System.Diagnostics;
using System.Data;
using OfficeOpenXml;

namespace PrePaymentModule.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly string _connectionString = "Data Source=192.168.7.35;Initial Catalog=Tosdb;User ID=devteam;Password=Test@123;TrustServerCertificate=True;";

        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult PrePaymentModule()
        {
            List<string> deferredExpenseAccounts = new List<string>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT AccCode,AccountName FROM ChartOfAccount WHERE AccCode BETWEEN 1310 AND 1399 AND AccountName not like 'Accrued%';";
                SqlCommand cmd = new SqlCommand(query, conn);

                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();
                   
                while (reader.Read())
                {
                    string formattedAccount = $"{reader["AccCode"]}-{reader["AccountName"]}";
                    deferredExpenseAccounts.Add(formattedAccount);
                }
            }

            ViewBag.DeferredExpenseAccounts = deferredExpenseAccounts;
            List<string> ExpenseAccounts = new List<string>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT AccCode, AccountName FROM ChartOfAccount WHERE AccCode BETWEEN 5120 AND 8940 ORDER BY AccCode ASC;";
                SqlCommand cmd = new SqlCommand(query, conn);

                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    string formattedAccount = $"{reader["AccCode"]}-{reader["AccountName"]}";
                    ExpenseAccounts.Add(formattedAccount);
                }
            }

            ViewBag.ExpenseAccounts = ExpenseAccounts;
            return View();
        }

        //[HttpPost]
        //public JsonResult GetSubtype(string accountTypeName)
        //{
        //    var subtypes = new List<object>();

        //    using (SqlConnection conn = new SqlConnection(_connectionString))
        //    {
        //        string query = @"
        //    SELECT ch.AccCode, ch.AccountName 
        //    FROM AccountType ac
        //    INNER JOIN ChartOfAccount ch ON ac.AccountTypeId = ch.AccountTypeId
        //    WHERE ac.Name = @AccountTypeName;";

        //        using (SqlCommand cmd = new SqlCommand(query, conn))
        //        {
        //            cmd.Parameters.AddWithValue("@AccountTypeName", accountTypeName);
        //            conn.Open();

        //            using (SqlDataReader reader = cmd.ExecuteReader())
        //            {
        //                while (reader.Read())
        //                {
        //                    var subtype = new
        //                    {
        //                        AccCode = reader["AccCode"].ToString(),
        //                        AccountName = reader["AccountName"].ToString()
        //                    };
        //                    subtypes.Add(subtype);
        //                }
        //            }
        //        }
        //    }

        //    return Json(subtypes);
        //}
        public IActionResult ComputeDeferral()
        {
            return View();
        }

        
        [HttpPost]
        public IActionResult Prepayment(Prepayment prepayment)
        {
            int lastInstallmentCode = _context.Installments
                .OrderByDescending(i => i.InstallmentCOde) // Ensure property name is correct
                .Select(i => i.InstallmentCOde)
                .FirstOrDefault();

            int nextInstallmentCode = lastInstallmentCode + 1; // Improved naming

            if (ModelState.IsValid)
            {
                prepayment.InstallmentCOde = nextInstallmentCode;

                _context.PrePayments.Add(prepayment);

                try
                {
                    _context.SaveChanges();
                    return RedirectToAction("AllPrePaymentRecords");
                }
                catch (Exception ex)
                {
                    // Log the exception details
                    // Example: _logger.LogError(ex, "Error saving prepayment.");

                    ModelState.AddModelError("", "An error occurred while saving. Please try again.");
                    // Optionally, you could give specific feedback based on exception type
                }
            }

            return View(prepayment);
        }


        [HttpPost]
        public JsonResult GenerateInstallments(string timeUnit, int recognitions, decimal originalValue, decimal currentValue, string acquisitionDate)
        {
            if (recognitions <= 0)
            {
                return Json(new { error = "Number of recognitions must be greater than zero." });
            }

            if (!DateTime.TryParse(acquisitionDate, out DateTime startDate))
            {
                return Json(new { error = "Invalid first recognition date." });
            }

            int lastInstallmentCode = _context.Installments
               .OrderByDescending(i => i.InstallmentCOde) // Assuming InstallmentCode is the property name
               .Select(i => i.InstallmentCOde)
               .FirstOrDefault();

            List<Installment> installments = new List<Installment>();
            decimal valueToUse = currentValue > 0 ? currentValue : originalValue;
            decimal installmentAmount = valueToUse / recognitions;
            decimal remainingBalance = valueToUse;

            for (int i = 0; i < recognitions; i++)
            {
                remainingBalance -= installmentAmount;

                // Calculate the last day of the month for the due date
                var dueDate = new DateTime(startDate.Year, startDate.Month, DateTime.DaysInMonth(startDate.Year, startDate.Month)).AddMonths(i);

                var newInstallment = new Installment
                {
                    InstallmentCOde = lastInstallmentCode + 1,
                    OriginalAmount = valueToUse,
                    Amount = installmentAmount,
                    DueDate = dueDate,
                    RemainingBalance = Math.Max(remainingBalance, 0)
                };

                installments.Add(newInstallment);
            }

            try
            {
                // Add the installments directly
                _context.Installments.AddRange(installments);
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                return Json(new { error = "Failed to save installments: " + ex.Message });
            }

            return Json(installments);
        }

        public IActionResult AllPrePaymentRecords()
        {
            try
            {
                var prepayments = _context.PrePayments.ToList();
                return View(prepayments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching prepayment records.");
                return StatusCode(500, "An error occurred while processing your request.");
            }
        }

        
        public IActionResult Installments(int installmentCode)
        {
            try
            {
                var records =  _context.Installments
                    .Where(i => i.InstallmentCOde == installmentCode) // Assuming 'Code' is the field for the installment code
                    .ToList();

                if (records == null || !records.Any())
                {
                    return NotFound("No records found.");
                }

                return View(records);
            }
            catch (Exception ex)
            {
                // Log the exception (you can use any logging framework)
                Console.WriteLine(ex.Message);
                return StatusCode(500, "Internal server error");
            }
        }


        //[HttpPost]
        //public IActionResult MarkAsPaid(int installmentCode)
        //{
        //    // Logic to mark the installment as paid in the database
        //    // Example:
        //    var installment = _context.Installments.FirstOrDefault(i => i.InstallmentCOde == installmentCode);
        //    if (installment != null)
        //    {
        //        installment.Status = "PAID"; // Update status
        //        _context.SaveChanges();
        //        return Ok();
        //    }
        //    return NotFound();
        //}

        //[HttpGet]
        //public IActionResult GetPaymentHistory()
        //{
        //    var history = _context.Installments
        //        .Where(i => i.Status == "PAID")
        //        .Select(i => new
        //        {
        //            i.InstallmentCOde,
        //            i.OriginalAmount,
        //            i.Amount,
        //            i.DueDate,
        //            i.RemainingBalance,
        //            i.Status
        //        }).ToList();

        //    return Json(history);
        //}

        [HttpPost]

        
        public IActionResult MarkAsPaid(int installmentCode, DateTime dueDate)
        {
            var installment = _context.Installments
                .SingleOrDefault(i => i.InstallmentCOde == installmentCode && i.DueDate == dueDate);

            if (installment == null)
            {
                return NotFound($"No installment found with code {installmentCode} and due date {dueDate:dd/MM/yyyy}.");
            }

            var historyEntry = new History
            {
                InstallmentCode = installment.InstallmentCOde,
                OriginalAmount = installment.OriginalAmount,
                Amount = installment.Amount,
                DueDate = installment.DueDate,
                RemainingBalance = installment.RemainingBalance,
                Status = "Paid"
            };

            _context.History.Add(historyEntry);
            _context.Installments.Remove(installment);
            _context.SaveChanges();

            return Ok();
        }

        [HttpGet]
        public IActionResult GetInstallmentHistory(int installmentCode)
        {
            try
            {
                ViewBag.InstallmentCode = installmentCode;  
                var records = _context.History
                    .Where(i => i.InstallmentCode == installmentCode) // Assuming 'Code' is the field for the installment code
                    .ToList();

                if (records == null || !records.Any())
                {
                    return NotFound("No records found.");
                }

                return View(records);
            }
            catch (Exception ex)
            {
                // Log the exception (you can use any logging framework)
                Console.WriteLine(ex.Message);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost]
        public async Task<IActionResult> GenerateReport(DateTime startDate, DateTime endDate)
           {
            DataTable reportData = new DataTable();

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand("PRACTICEGeneratePrePaymentReport", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@StartDate", startDate);
                    command.Parameters.AddWithValue("@EndDate", endDate);

                    await connection.OpenAsync();

                    using (var adapter = new SqlDataAdapter(command))
                    {
                        await Task.Run(() => adapter.Fill(reportData));
                    }
                }
            }
            ViewBag.StartDate = startDate.Date.ToString("dd-MM-yyyy"); 
            ViewBag.EndDate = endDate.Date.ToString("dd-MM-yyyy");
            return View(reportData); 
        }

        private string ExtractFourDigitCode(string accountInfo)
        {
            // Check if the accountInfo is null or empty
            if (string.IsNullOrWhiteSpace(accountInfo))
                return string.Empty;

            // Use regular expression to find a 4-digit code
            var match = System.Text.RegularExpressions.Regex.Match(accountInfo, @"\b\d{4}\b");
            return match.Success ? match.Value : string.Empty; // Return the matched code or empty string if not found
        }


        [HttpPost]
        public async Task<IActionResult> SaveEntries(int installmentCode)
        {
            var prepayment = new Prepayment();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var command = new SqlCommand("SELECT * FROM PrePayments WHERE InstallmentCode = @InstallmentCode", connection);
                command.Parameters.AddWithValue("@InstallmentCode", installmentCode);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        prepayment.Id = reader.GetInt32(reader.GetOrdinal("Id"));
                        prepayment.Voucher = reader.GetString(reader.GetOrdinal("Voucher"));
                        prepayment.ExpenseAccount = ExtractFourDigitCode(reader.GetString(reader.GetOrdinal("ExpenseAccount")));
                        prepayment.DeferredExpenseAccount = ExtractFourDigitCode(reader.GetString(reader.GetOrdinal("DeferredExpenseAccount")));

                        prepayment.DepreciatedAmount = reader.IsDBNull(reader.GetOrdinal("DepreciatedAmount")) ? (decimal?)null : reader.GetDecimal(reader.GetOrdinal("DepreciatedAmount"));
                    }
                    else
                    {
                        ModelState.AddModelError("", "Prepayment not found for the provided Installment Code.");
                        return View();
                    }
                }
            }

            int nextVoucherNo;
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var voucherCommand = new SqlCommand("SELECT COALESCE(MAX(VoucherNo), 0) + 1 FROM Vouchers", connection);
                nextVoucherNo = (int)await voucherCommand.ExecuteScalarAsync();
            }

            // Create Debit and Credit Transactions
            var debitTransaction = new Voucher
            {
                VoucherNo = nextVoucherNo,
                VoucherDate = DateTime.Now,
                VoucherType = "GV",
                ServiceType = "E",
                Account = prepayment.ExpenseAccount,
                Debit = prepayment.DepreciatedAmount ?? 0,
                Narration = "Recording of Prepaid Insurance Expense for " + DateTime.Now.ToString("MMMM yyyy")
            };

            var creditTransaction = new Voucher
            {
                VoucherNo = nextVoucherNo,
                VoucherDate = DateTime.Now,
                VoucherType = "GV",
                ServiceType = "A",
                Account = prepayment.DeferredExpenseAccount,
                Credit = prepayment.DepreciatedAmount ?? 0,
                Narration = "Recording of Prepaid Insurance Expense for " + DateTime.Now.ToString("MMMM yyyy")
            };

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var insertCommand = new SqlCommand("INSERT INTO Vouchers (VoucherNo, VoucherDate, VoucherType, ServiceType, Account, Debit, Credit, Narration) VALUES (@VoucherNo, @VoucherDate, @VoucherType, @ServiceType, @Account, @Debit, @Credit, @Narration)", connection, transaction);

                        // Insert Debit Transaction
                        insertCommand.Parameters.Clear();
                        insertCommand.Parameters.AddWithValue("@VoucherNo", debitTransaction.VoucherNo);
                        insertCommand.Parameters.AddWithValue("@VoucherDate", debitTransaction.VoucherDate.ToString());
                        insertCommand.Parameters.AddWithValue("@VoucherType", debitTransaction.VoucherType);
                        insertCommand.Parameters.AddWithValue("@ServiceType", debitTransaction.ServiceType);
                        insertCommand.Parameters.AddWithValue("@Account", debitTransaction.Account);
                        insertCommand.Parameters.AddWithValue("@Debit", debitTransaction.Debit);
                        insertCommand.Parameters.AddWithValue("@Credit", 0);
                        insertCommand.Parameters.AddWithValue("@Narration", debitTransaction.Narration);
                        await insertCommand.ExecuteNonQueryAsync();

                        // Insert Credit Transaction
                        insertCommand.Parameters.Clear();
                        insertCommand.Parameters.AddWithValue("@VoucherNo", creditTransaction.VoucherNo);
                        insertCommand.Parameters.AddWithValue("@VoucherDate", creditTransaction.VoucherDate.ToString());
                        insertCommand.Parameters.AddWithValue("@VoucherType", creditTransaction.VoucherType);
                        insertCommand.Parameters.AddWithValue("@ServiceType", creditTransaction.ServiceType);
                        insertCommand.Parameters.AddWithValue("@Account", creditTransaction.Account);
                        insertCommand.Parameters.AddWithValue("@Debit", 0);
                        insertCommand.Parameters.AddWithValue("@Credit", creditTransaction.Credit);
                        insertCommand.Parameters.AddWithValue("@Narration", creditTransaction.Narration);
                        await insertCommand.ExecuteNonQueryAsync();

                        await transaction.CommitAsync();

                        // Generate Excel file after successful save
                        var filePath = Path.Combine(Path.GetTempPath(), "PrepaymentReport.xlsx");
                        using (var package = new ExcelPackage())
                        {
                            var worksheet = package.Workbook.Worksheets.Add("Prepayment Details");
                            string _date = debitTransaction.VoucherDate.ToString();
                            // Add headers
                            worksheet.Cells[1, 1].Value = "Voucher No";
                            worksheet.Cells[1, 2].Value = "Voucher Date";
                            worksheet.Cells[1, 3].Value = "Voucher Type";
                            worksheet.Cells[1, 4].Value = "Service Type";
                            worksheet.Cells[1, 5].Value = "Account";
                            worksheet.Cells[1, 6].Value = "Debit";
                            worksheet.Cells[1, 7].Value = "Credit";
                            worksheet.Cells[1, 8].Value = "Narration";

                            // Add data for Debit Transaction
                            worksheet.Cells[2, 1].Value = debitTransaction.VoucherNo;
                            worksheet.Cells[2, 2].Value = _date;
                            worksheet.Cells[2, 3].Value = debitTransaction.VoucherType;
                            worksheet.Cells[2, 4].Value = debitTransaction.ServiceType;
                            worksheet.Cells[2, 5].Value = debitTransaction.Account;
                            worksheet.Cells[2, 6].Value = debitTransaction.Debit;
                            worksheet.Cells[2, 7].Value = debitTransaction.Credit;
                            worksheet.Cells[2, 8].Value = debitTransaction.Narration;

                            // Add data for Credit Transaction
                            worksheet.Cells[3, 1].Value = creditTransaction.VoucherNo;
                            worksheet.Cells[3, 2].Value = _date;
                            worksheet.Cells[3, 3].Value = creditTransaction.VoucherType;
                            worksheet.Cells[3, 4].Value = creditTransaction.ServiceType;
                            worksheet.Cells[3, 5].Value = creditTransaction.Account;
                            worksheet.Cells[3, 6].Value = creditTransaction.Debit;
                            worksheet.Cells[3, 7].Value = creditTransaction.Credit;
                            worksheet.Cells[3, 8].Value = creditTransaction.Narration;

                            // Save the Excel file
                            package.SaveAs(new FileInfo(filePath));
                        }

                        // Read the file bytes
                        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);

                        // Clean up the file after reading it
                        System.IO.File.Delete(filePath);

                        // Return the file to the client
                        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PrepaymentReport.xlsx");
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        ModelState.AddModelError("", "An error occurred while saving. Please try again.");
                        // Log the exception (optional)
                        Console.WriteLine(ex.Message);
                    }
                }
            }

            // If we reach here, there was an error. Return the view.
            return View();
        }






        //[HttpPost]
        //public async Task<IActionResult> SaveEntries(int installmentCode)
        //{
        //    var prepayment = new Prepayment();


        //    using (var connection = new SqlConnection(_connectionString))
        //    {
        //        await connection.OpenAsync();
        //        var command = new SqlCommand("SELECT * FROM PrePayments WHERE InstallmentCode = @InstallmentCode", connection);
        //        command.Parameters.AddWithValue("@InstallmentCode", installmentCode);

        //        using (var reader = await command.ExecuteReaderAsync())
        //        {
        //            if (await reader.ReadAsync())
        //            {
        //                // Assuming your PrePayment class has a similar structure
        //                prepayment.Id = reader.GetInt32(reader.GetOrdinal("Id"));
        //                prepayment.Voucher = reader.GetString(reader.GetOrdinal("Voucher"));
        //                prepayment.ExpenseAccount = reader.GetString(reader.GetOrdinal("ExpenseAccount"));
        //                prepayment.DeferredExpenseAccount = reader.GetString(reader.GetOrdinal("DeferredExpenseAccount"));
        //                prepayment.DepreciatedAmount = reader.IsDBNull(reader.GetOrdinal("DepreciatedAmount")) ? (decimal?)null : reader.GetDecimal(reader.GetOrdinal("DepreciatedAmount"));
        //            }
        //            else
        //            {
        //                ModelState.AddModelError("", "Prepayment not found for the provided Installment Code.");
        //                return View(); // Or redirect to another view
        //            }
        //        }
        //    }

        //    int nextVoucherNo;
        //    using (var connection = new SqlConnection(_connectionString))
        //    {
        //        await connection.OpenAsync();
        //        var voucherCommand = new SqlCommand("SELECT COALESCE(MAX(VoucherNo), 0) + 1 FROM Vouchers", connection);
        //        nextVoucherNo = (int)await voucherCommand.ExecuteScalarAsync();
        //    }
        //    // Create Debit Transaction
        //    var debitTransaction = new Voucher
        //    {
        //        VoucherNo = nextVoucherNo, 
        //        VoucherDate = DateTime.Now,
        //        VoucherType = "GV",
        //        ServiceType = "E",
        //        Party = "",
        //        Account = prepayment.ExpenseAccount,
        //        Department = "",
        //        ChequeOrReference = "",
        //        Debit = prepayment.DepreciatedAmount ?? 0,
        //        Credit = 0.00m,
        //        Narration = "Recording of Prepaid Insurance Expense for the month of " + DateTime.Now.ToString("MMMM yyyy")
        //    };

        //    // Create Credit Transaction
        //    var creditTransaction = new Voucher
        //    {
        //        VoucherNo = nextVoucherNo,
        //        VoucherDate = DateTime.Now,
        //        VoucherType = "GV",
        //        ServiceType = "A",
        //        Party = "",
        //        Account = prepayment.DeferredExpenseAccount, 
        //        Department = "",
        //        ChequeOrReference = "",
        //        Debit = 0.00m,
        //        Credit = prepayment.DepreciatedAmount ?? 0,
        //        Narration = "Recording of Prepaid Insurance Expense for the month of " + DateTime.Now.ToString("MMMM yyyy")
        //    };

        //    // Add transactions to the database using ADO.NET
        //    using (var connection = new SqlConnection(_connectionString))
        //    {
        //        await connection.OpenAsync();
        //        using (var transaction = connection.BeginTransaction())
        //        {
        //            try
        //            {
        //                var insertCommand = new SqlCommand("INSERT INTO Vouchers (VoucherNo, VoucherDate, VoucherType, ServiceType, Party, Account, Department, ChequeOrReference, Debit, Credit, Narration) VALUES (@VoucherNo, @VoucherDate, @VoucherType, @ServiceType, @Party, @Account, @Department, @ChequeOrReference, @Debit, @Credit, @Narration)", connection, transaction);

        //                // Insert Debit Transaction
        //                insertCommand.Parameters.Clear();
        //                insertCommand.Parameters.AddWithValue("@VoucherNo", debitTransaction.VoucherNo);
        //                insertCommand.Parameters.AddWithValue("@VoucherDate", debitTransaction.VoucherDate);
        //                insertCommand.Parameters.AddWithValue("@VoucherType", debitTransaction.VoucherType);
        //                insertCommand.Parameters.AddWithValue("@ServiceType", debitTransaction.ServiceType);
        //                insertCommand.Parameters.AddWithValue("@Party", debitTransaction.Party);
        //                insertCommand.Parameters.AddWithValue("@Account", debitTransaction.Account);
        //                insertCommand.Parameters.AddWithValue("@Department", debitTransaction.Department);
        //                insertCommand.Parameters.AddWithValue("@ChequeOrReference", debitTransaction.ChequeOrReference);
        //                insertCommand.Parameters.AddWithValue("@Debit", debitTransaction.Debit);
        //                insertCommand.Parameters.AddWithValue("@Credit", debitTransaction.Credit);
        //                insertCommand.Parameters.AddWithValue("@Narration", debitTransaction.Narration);
        //                await insertCommand.ExecuteNonQueryAsync();

        //                // Insert Credit Transaction
        //                insertCommand.Parameters.Clear();
        //                insertCommand.Parameters.AddWithValue("@VoucherNo", creditTransaction.VoucherNo);
        //                insertCommand.Parameters.AddWithValue("@VoucherDate", creditTransaction.VoucherDate);
        //                insertCommand.Parameters.AddWithValue("@VoucherType", creditTransaction.VoucherType);
        //                insertCommand.Parameters.AddWithValue("@ServiceType", creditTransaction.ServiceType);
        //                insertCommand.Parameters.AddWithValue("@Party", creditTransaction.Party);
        //                insertCommand.Parameters.AddWithValue("@Account", creditTransaction.Account);
        //                insertCommand.Parameters.AddWithValue("@Department", creditTransaction.Department);
        //                insertCommand.Parameters.AddWithValue("@ChequeOrReference", creditTransaction.ChequeOrReference);
        //                insertCommand.Parameters.AddWithValue("@Debit", creditTransaction.Debit);
        //                insertCommand.Parameters.AddWithValue("@Credit", creditTransaction.Credit);
        //                insertCommand.Parameters.AddWithValue("@Narration", creditTransaction.Narration);
        //                await insertCommand.ExecuteNonQueryAsync();

        //                await transaction.CommitAsync();


        //                return Ok(); 
        //            }
        //            catch (Exception ex)
        //            {
        //                await transaction.RollbackAsync();

        //                ModelState.AddModelError("", "An error occurred while saving. Please try again.");
        //            }
        //        }
        //    }

        //    return View();
        //}




        //private string GenerateExcelFile(Voucher debit, Voucher credit)
        //    {
        //        var filePath = Path.Combine(Path.GetTempPath(), "Vouchers.xlsx");

        //        using (var package = new ExcelPackage())
        //        {
        //            var worksheet = package.Workbook.Worksheets.Add("Vouchers");
        //            worksheet.Cells[1, 1].Value = "Voucher Date";
        //            worksheet.Cells[1, 2].Value = "Voucher Type";
        //            worksheet.Cells[1, 3].Value = "Service Type";
        //            worksheet.Cells[1, 4].Value = "Party";
        //            worksheet.Cells[1, 5].Value = "Account";
        //            worksheet.Cells[1, 6].Value = "Department";
        //            worksheet.Cells[1, 7].Value = "Cheque/Reference";
        //            worksheet.Cells[1, 8].Value = "Debit";
        //            worksheet.Cells[1, 9].Value = "Credit";
        //            worksheet.Cells[1, 10].Value = "Narration";

        //            worksheet.Cells[2, 1].Value = debit.VoucherDate;
        //            worksheet.Cells[2, 2].Value = debit.VoucherType;
        //            worksheet.Cells[2, 3].Value = debit.ServiceType;
        //            worksheet.Cells[2, 4].Value = debit.Party;
        //            worksheet.Cells[2, 5].Value = debit.Account;
        //            worksheet.Cells[2, 6].Value = debit.Department;
        //            worksheet.Cells[2, 7].Value = debit.ChequeOrReference;
        //            worksheet.Cells[2, 8].Value = debit.Debit;
        //            worksheet.Cells[2, 9].Value = debit.Credit;
        //            worksheet.Cells[2, 10].Value = debit.Narration;

        //            worksheet.Cells[3, 1].Value = credit.VoucherDate;
        //            worksheet.Cells[3, 2].Value = credit.VoucherType;
        //            worksheet.Cells[3, 3].Value = credit.ServiceType;
        //            worksheet.Cells[3, 4].Value = credit.Party;
        //            worksheet.Cells[3, 5].Value = credit.Account;
        //            worksheet.Cells[3, 6].Value = credit.Department;
        //            worksheet.Cells[3, 7].Value = credit.ChequeOrReference;
        //            worksheet.Cells[3, 8].Value = credit.Debit;
        //            worksheet.Cells[3, 9].Value = credit.Credit;
        //            worksheet.Cells[3, 10].Value = credit.Narration;

        //            package.SaveAs(new FileInfo(filePath));
        //        }

        //        return filePath;
        //    }

        //[HttpPost]
        //public async Task<IActionResult> SaveEntries(Prepayment prepayment)
        //{
        //    // Validate the incoming Prepayment model
        //    if (!ModelState.IsValid)
        //    {
        //        return View(prepayment); // Ya kisi aur view par redirect karein
        //    }

        //    // Debit Transaction
        //    var debitTransaction = new Voucher
        //    {
        //        VoucherNo = 1,
        //        VoucherDate = DateTime.Now,
        //        VoucherType = "Debit",
        //        ServiceType = "Service A",
        //        Party = prepayment.Voucher, 
        //        Account = prepayment.ExpenseAccount, 
        //        Department = "Department A", 
        //        ChequeOrReference = "Ref A", 
        //        Debit = prepayment.DepreciatedAmount ?? 0, 
        //        Credit = 0.00m,
        //        Narration = "Debit Entry for Prepayment ID: " + prepayment.Id
        //    };

        //    // Credit Transaction
        //    var creditTransaction = new Voucher
        //    {
        //        VoucherNo = 1,
        //        VoucherDate = DateTime.Now,
        //        VoucherType = "Credit",
        //        ServiceType = "Service B",
        //        Party = prepayment.Voucher,
        //        Account = "DeferredExpenseAccount", 
        //        Department = "Department B", 
        //        ChequeOrReference = "Ref B", 
        //        Debit = 0.00m,
        //        Credit = prepayment.DepreciatedAmount ?? 0, 
        //        Narration = "Credit Entry for Prepayment ID: " + prepayment.Id
        //    };

        //    // Add transactions to the database
        //    await _context.Vouchers.AddAsync(debitTransaction);
        //    await _context.Vouchers.AddAsync(creditTransaction);

        //    try
        //    {
        //        // Save changes to the database
        //        await _context.SaveChangesAsync();

        //        // Excel file generation
        //        var filePath = GenerateExcelFile(debitTransaction, creditTransaction);
        //        return File(System.IO.File.ReadAllBytes(filePath), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Vouchers.xlsx");
        //    }
        //    catch (Exception ex)
        //    {
        //        // Log the exception details
        //        // Example: _logger.LogError(ex, "Error saving entries.");
        //        ModelState.AddModelError("", "An error occurred while saving. Please try again.");
        //    }

        //    return View(prepayment);
        //}


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
