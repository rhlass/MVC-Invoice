using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using InvoiceApplication.Data;
using InvoiceApplication.Models;
using System.Diagnostics;
using InvoiceApplication.Services;

namespace InvoiceApplication.Controllers
{
    public class InvoiceController : Controller
    {
        private ApplicationDbContext _context;
        private IMySettingsService mySettingsService;

        public InvoiceController(ApplicationDbContext context, IMySettingsService settingsService)
        {
            _context = context;
            mySettingsService = settingsService;
        }

        // GET: Invoice
        public async Task<IActionResult> Index(string sortOrder, string searchQuery)
        {
            ViewBag.BeginSortParm = String.IsNullOrEmpty(sortOrder) ? "begin_desc" : "";

            ViewBag.NumberSortParm = sortOrder == "Number" ? "number_desc" : "Number";
            ViewBag.TotalSortParm = sortOrder == "Total" ? "total_desc" : "Total";
            ViewBag.DebtorSortParm = sortOrder == "Debtor" ? "debtor_desc" : "Debtor";

            var invoices = _context.Invoices.Include(i => i.Debtor).Include(i => i.InvoiceItems).ThenInclude(c => c.Product);
            var query = from invoice in invoices
                        select invoice;

            if (!String.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(i => i.Debtor.FullName.Contains(searchQuery)
                                    || i.InvoiceNumber.ToString().Contains(searchQuery)
                                    || i.Total.ToString().Contains(searchQuery));
            }

            switch (sortOrder)
            {
                //WHEN NO SORT
                case "begin_desc":
                    query = query.OrderBy(s => s.InvoiceNumber);
                    break;
                //INVOICE NUMBER
                case "Number":
                    query = query.OrderBy(s => s.InvoiceNumber);
                    break;
                case "number_desc":
                    query = query.OrderByDescending(s => s.InvoiceNumber);
                    break;
                //DEBTOR
                case "Debtor":
                    query = query.OrderBy(s => s.Debtor.FullName);
                    break;
                case "debtor_desc":
                    query = query.OrderByDescending(s => s.Debtor.FullName);
                    break;
                //TOTAL
                case "Total":
                    query = query.OrderBy(s => s.Total);
                    break;
                case "total_desc":
                    query = query.OrderByDescending(s => s.Total);
                    break;
                //DEFAUlT
                default:
                    query = query.OrderBy(s => s.InvoiceNumber);
                    break;
            }

            return View(await query.ToListAsync());
        }

        // GET: Invoice/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var invoice = await _context.Invoices
                .Include(d => d.Debtor)
                .Include(d => d.InvoiceItems)
                    .ThenInclude(c => c.Product)
                .SingleOrDefaultAsync(m => m.InvoiceNumber == id);

            if (invoice == null)
            {
                return NotFound();
            }

            return View(invoice);
        }

        // GET: Invoice/Create
        public IActionResult Create()
        {
            var products = _context.Products
                  .Select(s => new SelectListItem
                  {
                      Value = s.ProductID.ToString() + "_" + s.Price.ToString(),
                      Text = s.Name
                  });

            ViewBag.Products = new SelectList(products, "Value", "Text");
            ViewData["DebtorID"] = new SelectList(_context.Debtors, "DebtorID", "FullName");

            return View();
        }

        // POST: Invoice/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("InvoiceNumber,CreatedOn,DebtorID,ExpirationDate,Type")] Invoice invoice, string total, string pids, string amounts)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    invoice.Total = decimal.Parse(total);
                    _context.Add(invoice);
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }

                try
                {
                    string[] pidArray = pids.Split(',');
                    string[] amountArray = amounts.Split(',');

                    List<InvoiceItem> items = new List<InvoiceItem>();

                    for (int i = 0; i < pidArray.Length; i++)
                    {
                        int pid = int.Parse(pidArray[i]);
                        int amount = int.Parse(amountArray[i]);

                        InvoiceItem item = new InvoiceItem();
                        item.Amount = amount;
                        item.InvoiceNumber = invoice.InvoiceNumber;
                        item.ProductID = pid;

                        items.Add(item);
                    }

                    _context.AddRange(items);
                    await _context.SaveChangesAsync();

                    //SEND MAIL TO DEBTOR NOTIFYING ABOUT INVOICE
                    if (invoice.Type == "Final")
                    {
                        Debtor debtor = _context.Debtors.Single(d => d.DebtorID == invoice.DebtorID);
                        AuthMessageSender email = new AuthMessageSender(mySettingsService);
                        await email.SendInvoiceEmailAsync(debtor.Email);
                    }
                    

                    return RedirectToAction("Index");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }

            var products = _context.Products
                  .Select(s => new SelectListItem
                  {
                      Value = s.ProductID.ToString() + "_" + s.Price.ToString(),
                      Text = s.Name
                  });

            ViewBag.Products = new SelectList(products, "Value", "Text", pids);
            ViewData["DebtorID"] = new SelectList(_context.Debtors, "DebtorID", "FullName", invoice.DebtorID);
            return View(invoice);
        }

        // GET: Invoice/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var products = _context.Products
                 .Select(s => new SelectListItem
                 {
                     Value = s.ProductID.ToString() + "_" + s.Price.ToString(),
                     Text = s.Name
                 });

            var invoice = await _context.Invoices
                .Include(d => d.Debtor)
                .Include(d => d.InvoiceItems)
                    .ThenInclude(c => c.Product)
                .SingleOrDefaultAsync(m => m.InvoiceNumber == id);

            var items = await _context.InvoiceItems
                .Include(d => d.Product)
                .Where(m => m.InvoiceNumber == id)
                .ToArrayAsync();

            if (invoice == null)
            {
                return NotFound();
            }

            var p = _context.Products;
            string[] pids = new string[p.Count()];

            int cnt = 0;
            foreach (var pid in p)
            {
                string _id = pid.ProductID + "_" + pid.Price;
                pids[cnt] = _id;
                cnt++;
            }

            ViewBag.PIDs = pids;
            ViewBag.Amounts = items.Select(s => s.Amount).ToArray();
            ViewBag.Names = items.Select(s => s.Product.Name).ToArray();
            ViewBag.Total = String.Format("{0:N2}", invoice.Total);

            ViewBag.Products = new SelectList(products, "Value", "Text");
            ViewData["DebtorID"] = new SelectList(_context.Debtors, "DebtorID", "FullName", invoice.DebtorID);
            return View(invoice);
        }

        // POST: Invoice/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("InvoiceNumber,CreatedOn,DebtorID,ExpirationDate,Type")] Invoice invoice, string products, string amount, string total)
        {
            Debug.WriteLine("pids: " + products);
            Debug.WriteLine("amounts: " + amount);

            if (id != invoice.InvoiceNumber)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    invoice.Total = decimal.Parse(total);
                    _context.Update(invoice);
                    await _context.SaveChangesAsync();

                    _context.InvoiceItems.RemoveRange(_context.InvoiceItems.Where(c => c.InvoiceNumber == invoice.InvoiceNumber));

                    string[] pidArray = null;
                    string[] amountArray = null;
                    List<InvoiceItem> items = new List<InvoiceItem>();

                    if (products.Split('_')[0].Length > 1)
                    {
                        pidArray = products.Split('_')[0].Split(',');
                    }

                    if (amount.Length > 1)
                    {
                        amountArray = amount.Split(',');
                    }

                    if (pidArray != null)
                    {
                        for (int i = 0; i < pidArray.Length; i++)
                        {
                            int _pid = int.Parse(pidArray[i]);
                            int _amount = int.Parse(amountArray[i]);

                            InvoiceItem item = new InvoiceItem();
                            item.Amount = _amount;
                            item.InvoiceNumber = invoice.InvoiceNumber;
                            item.ProductID = _pid;

                            items.Add(item);
                        }

                        _context.InvoiceItems.AddRange(items);
                    }
                    else
                    {
                        InvoiceItem item = new InvoiceItem();
                        item.Amount = int.Parse(amount);
                        item.InvoiceNumber = invoice.InvoiceNumber;
                        item.ProductID = int.Parse(products.Split('_')[0]);

                       _context.InvoiceItems.Add(item);
                    }

                    //SEND MAIL TO DEBTOR NOTIFYING ABOUT INVOICE
                    if (invoice.Type == "Final")
                    {
                        string debtorEmail = invoice.Debtor.Email;
                        AuthMessageSender email = new AuthMessageSender(mySettingsService);
                        await email.SendInvoiceEmailAsync(debtorEmail);
                    }


                    await _context.SaveChangesAsync();

                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!InvoiceExists(invoice.InvoiceNumber))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction("Index");
            }
            ViewData["DebtorID"] = new SelectList(_context.Debtors, "DebtorID", "FullName", invoice.DebtorID);
            return View(invoice);
        }

        // GET: Invoice/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var invoice = await _context.Invoices
                .Include(d => d.Debtor)
                .Include(d => d.InvoiceItems)
                    .ThenInclude(c => c.Product)
                .SingleOrDefaultAsync(m => m.InvoiceNumber == id);

            if (invoice == null)
            {
                return NotFound();
            }

            return View(invoice);
        }

        // POST: Invoice/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var invoice = await _context.Invoices.SingleOrDefaultAsync(m => m.InvoiceNumber == id);
            var items = await _context.InvoiceItems.Where(i => i.InvoiceNumber == id).ToListAsync();

            _context.InvoiceItems.RemoveRange(items);
            _context.Invoices.Remove(invoice);

            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        private bool InvoiceExists(int id)
        {
            return _context.Invoices.Any(e => e.InvoiceNumber == id);
        }
    }
}
